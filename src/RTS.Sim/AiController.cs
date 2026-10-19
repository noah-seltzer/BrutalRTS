using RTS.Core;

namespace RTS.Sim;

/// <summary>Computer opponent. Runs inside World.Step (deterministic, uses World RNG).</summary>
internal sealed class AiController
{
    private readonly World _w;
    public readonly int Me;
    private readonly Difficulty _diff;
    private readonly float _interval;
    private readonly int _workerTarget;
    private int _waveThreshold;
    private readonly int _waveGrowth, _waveCap;
    private readonly float _firstWave;
    private readonly int _turrets;
    private float _timer;
    private float _retargetTimer;
    private readonly HashSet<int> _wave = new();
    private int _infantryToggle;
    private float _lastAttackedTime = -100f;
    private Vec2 _lastAttackedPos;
    private int _bonusAcc;

    public AiController(World w, int me, Difficulty diff)
    {
        _w = w; Me = me; _diff = diff;
        switch (diff)
        {
            case Difficulty.Easy: _interval = 1.0f; _workerTarget = 12; _waveThreshold = 6; _waveGrowth = 2; _waveCap = 16; _firstWave = 420f; _turrets = 1; break;
            case Difficulty.Hard: _interval = 0.34f; _workerTarget = 16; _waveThreshold = 14; _waveGrowth = 4; _waveCap = 40; _firstWave = 240f; _turrets = 3; break;
            default: _interval = 0.5f; _workerTarget = 14; _waveThreshold = 10; _waveGrowth = 3; _waveCap = 28; _firstWave = 300f; _turrets = 2; break;
        }
        _timer = 0.1f * me;
    }

    private int Foe => 1 - Me;
    private PlayerState P => _w.Players[Me];

    public int IncomeBonus(int amount)
    {
        if (_diff != Difficulty.Hard) return 0;
        _bonusAcc += amount; // +25%
        int b = _bonusAcc / 4; _bonusAcc -= b * 4;
        return b;
    }

    public void NotifyAttacked(Entity victim, Entity? src)
    {
        if (src == null || src.Owner != Foe) return;
        _lastAttackedTime = _w.Time;
        _lastAttackedPos = src.Pos;
    }

    public void Update(float dt)
    {
        _timer -= dt;
        _retargetTimer -= dt;
        if (_timer > 0) return;
        _timer = _interval;
        if (P.Defeated) return;
        Think();
    }

    // snapshot
    private readonly List<Entity> _workers = new(), _army = new(), _structs = new();
    private Entity? _hq;

    private void Think()
    {
        _workers.Clear(); _army.Clear(); _structs.Clear(); _hq = null;
        foreach (var e in _w.Entities)
        {
            if (!e.Alive || e.Owner != Me) continue;
            if (e.Def.IsStructure) { _structs.Add(e); if (e.Kind == EntityKind.HQ && (_hq == null || _hq.UnderConstruction)) _hq = e; }
            else if (e.Kind == EntityKind.Worker) _workers.Add(e);
            else _army.Add(e);
        }
        _wave.RemoveWhere(id => _w.Get(id) is not { Alive: true });
        Vec2 home = _hq?.Pos ?? (_structs.Count > 0 ? _structs[0].Pos : _w.StartLocation(Me));

        ManageWorkers(home);
        int reserve = BuildOrder(home);
        TrainWorkers(reserve);
        TrainArmy(reserve, home);
        Defend(home);
        Attack(home);
    }

    // ---------------- economy ----------------
    private void ManageWorkers(Vec2 home)
    {
        foreach (var wk in _workers)
        {
            if (wk.Order != UnitOrder.Idle) continue;
            var f = _w.NearestField(home, 30 * World.TileSize) ?? _w.NearestField(wk.Pos, 200 * World.TileSize);
            if (f != null) _w.CmdHarvest(new[] { wk.Id }, f.Id);
        }
    }

    private int QueuedCount(EntityKind k)
    {
        int n = 0;
        foreach (var s in _structs) foreach (var q in s.TrainQueue) if (q == k) n++;
        return n;
    }

    private void TrainWorkers(int reserve)
    {
        if (_hq == null || _hq.UnderConstruction) return;
        int total = _workers.Count + QueuedCount(EntityKind.Worker);
        if (total >= _workerTarget || _hq.TrainQueue.Count >= 1) return;
        int cost = Defs.Get(EntityKind.Worker).Cost;
        bool urgent = total < 8;
        if (P.Credits - cost >= reserve || (urgent && P.Credits >= cost))
            if (P.Pop + 1 <= P.PopCap) _w.CmdTrain(_hq.Id, EntityKind.Worker);
    }

    /// <summary>Count structures of kind including those under construction and pending (worker en route).</summary>
    private int CountStruct(EntityKind k)
    {
        int n = 0;
        foreach (var s in _structs) if (s.Kind == k) n++;
        foreach (var wk in _workers) if (wk.Order == UnitOrder.Build && wk.X.BuildSiteId < 0 && wk.X.BuildKind == k) n++;
        return n;
    }
    private int InProgress(EntityKind k)
    {
        int n = 0;
        foreach (var s in _structs) if (s.Kind == k && s.UnderConstruction) n++;
        foreach (var wk in _workers) if (wk.Order == UnitOrder.Build && wk.X.BuildSiteId < 0 && wk.X.BuildKind == k) n++;
        return n;
    }

    /// <summary>Returns the credits to reserve for the current top build priority (save-up semantics).</summary>
    private int BuildOrder(Vec2 home)
    {
        var goal = NextGoal();
        if (goal == null) return 0;
        int cost = Defs.Get(goal.Value).Cost;
        if (P.Credits >= cost)
        {
            if (TryBuild(goal.Value, home)) return 0;
            return 0; // couldn't place now; don't block spending
        }
        return cost;
    }

    private EntityKind? NextGoal()
    {
        int workers = _workers.Count;
        // pop: projected cap including in-progress depots
        int capProjected = Math.Min(Defs.MaxPop, P.PopCap + InProgress(EntityKind.SupplyDepot) * Defs.Get(EntityKind.SupplyDepot).PopProvided
                                    + InProgress(EntityKind.HQ) * Defs.Get(EntityKind.HQ).PopProvided);
        int queuedPop = 0;
        foreach (var s in _structs) for (int i = 0; i < s.TrainQueue.Count; i++) if (!(i == 0 && s.X.TrainStarted)) queuedPop += Defs.Get(s.TrainQueue[i]).PopCost;
        int used = P.Pop + queuedPop;
        int margin = 3 + CountStruct(EntityKind.Barracks) * 2 + CountStruct(EntityKind.Factory) * 3;
        int depotsInProg = InProgress(EntityKind.SupplyDepot);
        if (capProjected < Defs.MaxPop && capProjected - used <= margin && depotsInProg < (P.PopCap >= 40 ? 2 : 1)) return EntityKind.SupplyDepot;
        if (workers < 7) return null;
        bool barracksDone = _w.HasCompleted(Me, EntityKind.Barracks);
        if (CountStruct(EntityKind.Barracks) < 1) return EntityKind.Barracks;
        if (barracksDone && CountStruct(EntityKind.Factory) < 1) return EntityKind.Factory;
        if (barracksDone && CountStruct(EntityKind.Turret) < Math.Min(_turrets, 1)) return EntityKind.Turret;
        if (_w.Time > 200f && CountStruct(EntityKind.Barracks) < 2) return EntityKind.Barracks;
        if (barracksDone && CountStruct(EntityKind.Turret) < _turrets) return EntityKind.Turret;
        if (_diff == Difficulty.Hard && _w.Time > 360f && CountStruct(EntityKind.Factory) < 2 && _w.HasCompleted(Me, EntityKind.Factory)) return EntityKind.Factory;
        // floating credits -> more production (only when nothing else is pending)
        int maxBarracks = _diff switch { Difficulty.Easy => 2, Difficulty.Hard => 4, _ => 3 };
        if (_w.Time > 240f && P.Credits > 600 && CountStruct(EntityKind.Barracks) < maxBarracks && InProgress(EntityKind.Barracks) == 0) return EntityKind.Barracks;
        return null;
    }

    private bool TryBuild(EntityKind k, Vec2 home)
    {
        Entity? builder = null; float bd = float.MaxValue;
        foreach (var wk in _workers)
        {
            if (wk.Order == UnitOrder.Build) continue;
            float d = Vec2.DistanceSq(wk.Pos, home) + wk.Cargo * 500f + (wk.Order is UnitOrder.Idle or UnitOrder.Harvest ? 0 : 1e6f);
            if (d < bd) { bd = d; builder = wk; }
        }
        if (builder == null) return false;
        var spot = FindSpot(k, home);
        if (spot == null) return false;
        return _w.CmdBuild(builder.Id, k, spot.Value.x, spot.Value.y);
    }

    private (int x, int y)? FindSpot(EntityKind k, Vec2 home)
    {
        int s = Defs.Get(k).FootprintTiles;
        var (hx, hy) = World.WorldToTile(home);
        Vec2 enemyDir = (_w.StartLocation(Foe) - home).Normalized;
        Vec2 centerDir = (new Vec2(_w.Width * World.TileSize / 2f, _w.Height * World.TileSize / 2f) - home).Normalized;
        // spread non-turret buildings around; turrets face the enemy
        float jitter = (float)(_w.Rng.NextDouble() - 0.5) * 2.4f;
        Vec2 dir = k == EntityKind.Turret ? enemyDir : Rotate(centerDir, jitter);
        int minR = k == EntityKind.Turret ? 5 : 4;
        for (int r = minR; r <= 32; r++)
        {
            var pref = new Vec2(hx + dir.X * r, hy + dir.Y * r);
            (int, int)? best = null; float bestD = float.MaxValue;
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int tx = hx + dx - s / 2, ty = hy + dy - s / 2;
                    float d = (tx - pref.X) * (tx - pref.X) + (ty - pref.Y) * (ty - pref.Y);
                    if (d >= bestD) continue;
                    if (!AiCanPlace(k, tx, ty)) continue;
                    bestD = d; best = (tx, ty);
                }
            if (best != null) return best;
        }
        return null;
    }

    private static Vec2 Rotate(Vec2 v, float a) => new(v.X * MathF.Cos(a) - v.Y * MathF.Sin(a), v.X * MathF.Sin(a) + v.Y * MathF.Cos(a));

    private bool AiCanPlace(EntityKind k, int tx, int ty)
    {
        int s = Defs.Get(k).FootprintTiles;
        if (tx < 1 || ty < 1 || tx + s + 1 > _w.Width || ty + s + 1 > _w.Height) return false;
        // 1-tile free margin keeps paths open
        for (int x = tx - 1; x <= tx + s; x++)
            for (int y = ty - 1; y <= ty + s; y++)
                if (!_w.IsPassable(x, y)) return false;
        var c = World.FootprintCenter(k, tx, ty);
        foreach (var e in _w.Entities)
        {
            if (e.Kind != EntityKind.CrystalField || !e.Alive) continue;
            if (Vec2.Distance(e.Pos, c) < 5.5f * World.TileSize) return false;
        }
        // keep the lane between HQ and its fields clear
        if (_hq != null)
        {
            foreach (var e in _w.Entities)
            {
                if (e.Kind != EntityKind.CrystalField || !e.Alive || Vec2.Distance(e.Pos, _hq.Pos) > 12 * World.TileSize) continue;
                if (DistToSegment(c, _hq.Pos, e.Pos) < (s / 2f + 2f) * World.TileSize) return false;
            }
        }
        return _w.CanPlace(Me, k, tx, ty) && !OverlapsPendingWithMargin(k, tx, ty);
    }

    private bool OverlapsPendingWithMargin(EntityKind k, int tx, int ty)
    {
        int s = Defs.Get(k).FootprintTiles;
        foreach (var wk in _workers)
        {
            if (wk.Order != UnitOrder.Build || wk.X.BuildSiteId >= 0) continue;
            int os = Defs.Get(wk.X.BuildKind).FootprintTiles;
            if (tx - 1 < wk.X.BuildTx + os && wk.X.BuildTx < tx + s + 1 && ty - 1 < wk.X.BuildTy + os && wk.X.BuildTy < ty + s + 1) return true;
        }
        return false;
    }

    private static float DistToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a; float l2 = ab.LengthSq;
        if (l2 < 1e-4f) return Vec2.Distance(p, a);
        float t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / l2, 0, 1);
        return Vec2.Distance(p, a + ab * t);
    }

    // ---------------- army ----------------
    private void TrainArmy(int reserve, Vec2 home)
    {
        bool hasFactory = false;
        Entity? idleFactory = null;
        foreach (var s in _structs)
            if (s.Kind == EntityKind.Factory && !s.UnderConstruction) { hasFactory = true; if (s.TrainQueue.Count == 0) idleFactory ??= s; }
        int tankCost = Defs.Get(EntityKind.Tank).Cost;
        int freePop = P.PopCap - P.Pop;
        foreach (var s in _structs) for (int i = 0; i < s.TrainQueue.Count; i++) if (!(i == 0 && s.X.TrainStarted)) freePop -= Defs.Get(s.TrainQueue[i]).PopCost;
        var rally = home + (new Vec2(_w.Width * World.TileSize / 2f, _w.Height * World.TileSize / 2f) - home).Normalized * (5 * World.TileSize);

        // tanks first
        foreach (var s in _structs)
        {
            if (s.Kind != EntityKind.Factory || s.UnderConstruction) continue;
            if (s.RallyPoint == null) _w.CmdRally(s.Id, rally);
            if (s.TrainQueue.Count < 1 && P.Credits - tankCost >= reserve && freePop >= 3)
                if (_w.CmdTrain(s.Id, EntityKind.Tank)) freePop -= 3;
        }
        // infantry: keep a pop + credit reserve for tanks when a factory is idle
        int tankCredReserve = idleFactory != null ? tankCost : 0;
        int tankPopReserve = hasFactory ? 3 : 0;
        foreach (var s in _structs)
        {
            if (s.Kind != EntityKind.Barracks || s.UnderConstruction) continue;
            if (s.RallyPoint == null) _w.CmdRally(s.Id, rally);
            if (s.TrainQueue.Count >= 1) continue;
            var k = (_infantryToggle % 5) is 1 or 3 ? EntityKind.Ranger : EntityKind.Soldier;
            int cost = Defs.Get(k).Cost;
            if (P.Credits - cost >= reserve + tankCredReserve && freePop - 1 >= tankPopReserve)
                if (_w.CmdTrain(s.Id, k)) { freePop -= 1; _infantryToggle++; }
        }
    }

    private void Defend(Vec2 home)
    {
        // enemy units near any of my structures
        Entity? threat = null; float bd = float.MaxValue;
        foreach (var e in _w.Entities)
        {
            if (!e.Alive || e.Owner != Foe || e.Def.IsStructure) continue;
            foreach (var s in _structs)
            {
                float d = Vec2.DistanceSq(e.Pos, s.Pos);
                if (d < 13f * 13f * World.TileSize * World.TileSize)
                {
                    float dh = Vec2.DistanceSq(e.Pos, home);
                    if (dh < bd) { bd = dh; threat = e; }
                    break;
                }
            }
        }
        Vec2? target = threat?.Pos;
        if (target == null && _w.Time - _lastAttackedTime < 3f && Vec2.Distance(_lastAttackedPos, home) < 18 * World.TileSize) target = _lastAttackedPos;
        if (target == null) return;
        var ids = new List<int>();
        foreach (var u in _army)
        {
            if (_wave.Contains(u.Id) && Vec2.Distance(u.Pos, home) > 25 * World.TileSize) continue; // wave far away keeps attacking
            if (u.Order == UnitOrder.Attack || (u.Order == UnitOrder.AttackMove && u.TargetId.HasValue)) continue; // already fighting
            ids.Add(u.Id);
        }
        if (ids.Count > 0) _w.CmdMove(ids, target.Value, attackMove: true);
    }

    private Entity? NearestFoeStructure(Vec2 from)
    {
        Entity? best = null; float bd = float.MaxValue;
        foreach (var e in _w.Entities)
        {
            if (!e.Alive || e.Owner != Foe || !e.Def.IsStructure) continue;
            float d = Vec2.DistanceSq(e.Pos, from);
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    private void Attack(Vec2 home)
    {
        int homePop = 0;
        var homeIds = new List<int>();
        foreach (var u in _army) if (!_wave.Contains(u.Id)) { homePop += u.Def.PopCost; homeIds.Add(u.Id); }
        bool maxed = P.Pop >= Defs.MaxPop - 4 && homePop >= 10;
        if ((_w.Time >= _firstWave && homePop >= _waveThreshold) || maxed)
        {
            var target = NearestFoeStructure(home);
            if (target != null)
            {
                _w.CmdMove(homeIds, target.Pos, attackMove: true);
                foreach (var id in homeIds) _wave.Add(id);
                _waveThreshold = Math.Min(_waveCap, _waveThreshold + _waveGrowth);
            }
        }
        if (_retargetTimer <= 0)
        {
            _retargetTimer = 3f;
            foreach (var id in _wave)
            {
                var u = _w.Get(id);
                if (u == null || u.Order != UnitOrder.Idle) continue;
                var t = NearestFoeStructure(u.Pos);
                if (t != null) _w.CmdMove(new[] { u.Id }, t.Pos, attackMove: true);
            }
        }
    }
}
