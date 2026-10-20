using RTS.Core;

namespace RTS.Sim;

/// <summary>Sim-internal per-entity state (paths, timers, build bookkeeping).</summary>
internal sealed class EntityExtra
{
    public List<Vec2>? Path;
    public int PathIdx;
    public Vec2 PathGoal;
    public float RepathTimer;
    public Vec2 StuckPos;
    public float StuckTimer;
    public float Gather;
    public int LastField = -1;
    public EntityKind BuildKind;
    public int BuildTx, BuildTy;
    public int BuildSiteId = -1;
    public bool BuildPaid;
    public readonly List<QueuedOrder> Queue = new(); // shift-queued orders after the current one
    public float ScanTimer;
    public bool TrainStarted;
    public int TileX, TileY;       // structures: top-left footprint tile
    public bool CompletionHandled;  // structures
}

/// <summary>Visual-only record of a ranged attack (client may draw tracers). Cleared like Events.</summary>
public readonly record struct ShotFx(Vec2 From, Vec2 To, int Owner, EntityKind Kind);

public sealed partial class World
{
    // ---------------- added public helpers ----------------
    public Difficulty Difficulty { get; private set; }
    /// <summary>Tracer info for every ranged shot fired this step (append-only; client drains/clears like Events).</summary>
    public readonly List<ShotFx> Shots = new();
    /// <summary>When false, no AI controllers run (useful for tests).</summary>
    public bool AiEnabled { get; set; } = true;
    public bool IsGameOver => Winner.HasValue;

    /// <summary>Tile walkable (grass and no structure).</summary>
    public bool IsPassable(int tx, int ty) => InBounds(tx, ty) && _terrain[tx, ty] == Terrain.Grass && _occ[ty * Width + tx] == 0;
    public bool InBounds(int tx, int ty) => tx >= 0 && ty >= 0 && tx < Width && ty < Height;
    public static (int tx, int ty) WorldToTile(Vec2 p) => ((int)MathF.Floor(p.X / TileSize), (int)MathF.Floor(p.Y / TileSize));
    public static Vec2 TileCenter(int tx, int ty) => new((tx + 0.5f) * TileSize, (ty + 0.5f) * TileSize);

    /// <summary>Structure placement: (tileX,tileY) passed to CmdBuild/CanPlace is the TOP-LEFT tile of the footprint.
    /// This converts a world point (e.g. mouse) to the top-left tile that centers the footprint on it.</summary>
    public static (int tx, int ty) FootprintOriginFromCenter(EntityKind k, Vec2 worldCenter)
    {
        int s = Defs.Get(k).FootprintTiles;
        return ((int)MathF.Round(worldCenter.X / TileSize - s / 2f), (int)MathF.Round(worldCenter.Y / TileSize - s / 2f));
    }
    /// <summary>World-space center of a footprint whose top-left tile is (tx,ty).</summary>
    public static Vec2 FootprintCenter(EntityKind k, int tx, int ty)
    {
        int s = Defs.Get(k).FootprintTiles;
        return new((tx + s / 2f) * TileSize, (ty + s / 2f) * TileSize);
    }
    /// <summary>Top-left tile of a structure's footprint.</summary>
    public static (int tx, int ty) StructureOrigin(Entity e) => (e.X.TileX, e.X.TileY);

    /// <summary>Max HP (veterancy is cosmetic, so this is the base value).</summary>
    public static int MaxHpOf(Entity e) => e.Def.MaxHp;
    /// <summary>Base damage per hit, before bonus damage and armor (veterancy is cosmetic).</summary>
    public static int DamageOf(Entity e) => e.Def.Damage;
    /// <summary>Damage one hit from attacker deals to target: max(1, base + bonus vs target's attributes - target armor).</summary>
    public static int HitDamage(Entity attacker, Entity target) => HitDamageImpl(attacker, target);
    /// <summary>True if the owner has a COMPLETED structure of this kind.</summary>
    public bool HasCompleted(int owner, EntityKind k)
    {
        foreach (var e in _entities) if (e.Owner == owner && e.Kind == k && e.Alive && !e.UnderConstruction) return true;
        return false;
    }
    /// <summary>Tech requirement satisfied for building/training this kind.</summary>
    public bool MeetsRequirement(int owner, EntityKind k) { var r = Defs.Get(k).Requires; return r == null || HasCompleted(owner, r.Value); }
    /// <summary>Remaining waypoints of a unit's current path (for debug drawing), or null.</summary>
    public IReadOnlyList<Vec2>? PathOf(Entity e) => e.X.Path;
    /// <summary>HQ-ish start location of a player (world units).</summary>
    public Vec2 StartLocation(int player) => _startLoc[player];
    /// <summary>Attach an AI controller to a player (player 1 has one by default). Lets you run AI-vs-AI demos.</summary>
    public void AddAi(int player, Difficulty difficulty)
    {
        _ais.RemoveAll(a => a.Me == player);
        _ais.Add(new AiController(this, player, difficulty));
    }
    public void RemoveAi(int player) => _ais.RemoveAll(a => a.Me == player);

    // ---------------- internal state ----------------
    private readonly Dictionary<int, Entity> _byId = new();
    private int _nextId = 1;
    private Random _rng = null!;
    private int[] _occ = null!;
    private readonly Vec2[] _startLoc = new Vec2[2];
    private readonly List<Entity> _units = new();
    private readonly List<Entity> _structs = new();
    private readonly float[] _lastUnderAttack = { -100f, -100f };
    private readonly float[] _lastPopCapped = { -100f, -100f };
    private readonly List<AiController> _ais = new();
    private long _stepCount;

    internal Random Rng => _rng;

    private void InitWorld(int seed, Difficulty difficulty)
    {
        _rng = new Random(seed);
        Difficulty = difficulty;
        _occ = new int[Width * Height];
        InitPathfinding();
        InitFog();
        GenerateMap();
        foreach (var p in Players) { p.Credits = Defs.StartingCredits; p.Pop = 0; p.PopCap = 0; }
        RebuildLists();
        RecomputePop();
        RecomputeVisibility();
        _ais.Add(new AiController(this, 1, difficulty));
    }

    // ---------------- map generation ----------------
    private void GenerateMap()
    {
        int W = Width, H = Height;
        for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) _terrain[x, y] = Terrain.Grass;

        void Blob(Terrain t, int cx, int cy, int parts, int minR, int maxR, int spread)
        {
            for (int i = 0; i < parts; i++)
            {
                int ox = cx + _rng.Next(-spread, spread + 1), oy = cy + _rng.Next(-spread, spread + 1);
                int r = _rng.Next(minR, maxR + 1);
                for (int x = ox - r; x <= ox + r; x++)
                    for (int y = oy - r; y <= oy + r; y++)
                        if (InBounds(x, y) && (x - ox) * (x - ox) + (y - oy) * (y - oy) <= r * r + 1) _terrain[x, y] = t;
            }
        }
        int lakes = 6 + _rng.Next(4);
        for (int i = 0; i < lakes; i++) Blob(Terrain.Water, _rng.Next(6, W - 6), _rng.Next(6, H - 6), 3 + _rng.Next(3), 2, 5, 4);
        int rocks = 10 + _rng.Next(6);
        for (int i = 0; i < rocks; i++) Blob(Terrain.Rock, _rng.Next(4, W - 4), _rng.Next(4, H - 4), 2 + _rng.Next(3), 1, 3, 3);

        void Clear(float cx, float cy, float r)
        {
            for (int x = (int)(cx - r) - 1; x <= (int)(cx + r) + 1; x++)
                for (int y = (int)(cy - r) - 1; y <= (int)(cy + r) + 1; y++)
                    if (InBounds(x, y) && (x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) <= r * r) _terrain[x, y] = Terrain.Grass;
        }
        void Carve(float x0, float y0, float x1, float y1, float r)
        {
            float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int steps = (int)(len * 2) + 1;
            for (int i = 0; i <= steps; i++) { float t = i / (float)steps; Clear(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, r); }
        }

        // Player 0 layout (tiles). Player 1 mirrored.
        const int hqT = 11;
        (int x, int y)[] fields0 = { (5, 11), (6, 5), (11, 5) };
        (int x, int y)[] workers0 = { (15, 11), (15, 13), (11, 15), (13, 15) };
        (int x, int y)[] mid = { (44, 44), (50, 50), (20, 70), (74, 24), (60, 30), (34, 64) };

        float b0 = hqT + 1.5f, b1 = W - hqT - 1.5f;
        Clear(b0, b0, 11.5f);
        Clear(b1, b1, 11.5f);
        // two winding routes between the bases
        for (int route = 0; route < 2; route++)
        {
            float mx = W / 2f + _rng.Next(-14, 15) + (route == 0 ? -10 : 10), my = H / 2f + _rng.Next(-14, 15) + (route == 0 ? 10 : -10);
            Carve(b0, b0, mx, my, 2.2f);
            Carve(mx, my, b1, b1, 2.2f);
        }
        foreach (var m in mid) Clear(m.x + 1f, m.y + 1f, 4.5f);
        // Keep the map border passable-ish irrelevant; ensure mid fields connect to a route
        foreach (var m in mid) Carve(m.x + 1f, m.y + 1f, W / 2f, H / 2f, 1.6f);
        Clear(W / 2f, H / 2f, 3f);

        int Mir(int t, int size) => W - t - size;
        for (int p = 0; p < 2; p++)
        {
            int hx = p == 0 ? hqT : Mir(hqT, 3);
            var hq = SpawnStructure(EntityKind.HQ, p, hx, hx, complete: true);
            _startLoc[p] = hq.Pos;
            foreach (var f in fields0)
            {
                int fx = p == 0 ? f.x : Mir(f.x, 2), fy = p == 0 ? f.y : Mir(f.y, 2);
                var cf = SpawnStructure(EntityKind.CrystalField, -1, fx, fy, complete: true);
                cf.Resources = 1500;
            }
            foreach (var wk in workers0)
            {
                int wx = p == 0 ? wk.x : Mir(wk.x, 1), wy = p == 0 ? wk.y : Mir(wk.y, 1);
                SpawnUnit(EntityKind.Worker, p, TileCenter(wx, wy));
            }
        }
        foreach (var m in mid)
        {
            var cf = SpawnStructure(EntityKind.CrystalField, -1, m.x, m.y, complete: true);
            cf.Resources = 1500;
        }
    }

    // ---------------- entity management ----------------
    internal Entity SpawnUnit(EntityKind k, int owner, Vec2 pos)
    {
        var d = Defs.Get(k);
        var e = new Entity { Id = _nextId++, Kind = k, Def = d, Owner = owner, Pos = pos, Hp = d.MaxHp, BuildProgress = 1f, Order = UnitOrder.Idle };
        e.Facing = owner == 0 ? new Vec2(1, 0) : new Vec2(-1, 0);
        e.X.StuckPos = pos;
        _entities.Add(e);
        _byId[e.Id] = e;
        return e;
    }

    internal Entity SpawnStructure(EntityKind k, int owner, int tx, int ty, bool complete)
    {
        var d = Defs.Get(k);
        var e = new Entity
        {
            Id = _nextId++, Kind = k, Def = d, Owner = owner, Pos = FootprintCenter(k, tx, ty),
            BuildProgress = complete ? 1f : 0f, Hp = complete ? d.MaxHp : Math.Max(1, d.MaxHp / 10),
        };
        e.X.TileX = tx; e.X.TileY = ty;
        e.X.CompletionHandled = complete;
        int s = d.FootprintTiles;
        for (int x = tx; x < tx + s; x++)
            for (int y = ty; y < ty + s; y++)
            {
                _terrain[x, y] = Terrain.Grass == _terrain[x, y] ? Terrain.Grass : _terrain[x, y];
                _occ[y * Width + x] = e.Id;
            }
        _entities.Add(e);
        _byId[e.Id] = e;
        return e;
    }

    private Entity? FindEntity(int id) => _byId.TryGetValue(id, out var e) ? e : null;

    private void RebuildLists()
    {
        _units.Clear(); _structs.Clear();
        foreach (var e in _entities)
        {
            if (!e.Alive) continue;
            if (e.Def.IsStructure) { if (e.Kind != EntityKind.CrystalField) _structs.Add(e); }
            else _units.Add(e);
        }
        BuildSpatialGrid();
    }

    private void RemoveDead()
    {
        bool any = false;
        foreach (var e in _entities) if (!e.Alive) { any = true; break; }
        if (!any) return;
        for (int i = _entities.Count - 1; i >= 0; i--)
        {
            var e = _entities[i];
            if (e.Alive) continue;
            if (e.Def.IsStructure)
            {
                int s = e.Def.FootprintTiles;
                for (int x = e.X.TileX; x < e.X.TileX + s; x++)
                    for (int y = e.X.TileY; y < e.X.TileY + s; y++)
                        if (_occ[y * Width + x] == e.Id) _occ[y * Width + x] = 0;
            }
            _byId.Remove(e.Id);
            _entities.RemoveAt(i);
        }
    }

    private void RecomputePop()
    {
        Span<int> pop = stackalloc int[2];
        Span<int> cap = stackalloc int[2];
        foreach (var e in _entities)
        {
            if (!e.Alive || e.Owner < 0 || e.Owner > 1) continue;
            if (e.Def.IsStructure)
            {
                if (!e.UnderConstruction) cap[e.Owner] += e.Def.PopProvided;
                if (e.X.TrainStarted && e.TrainQueue.Count > 0) pop[e.Owner] += Defs.Get(e.TrainQueue[0]).PopCost;
            }
            else pop[e.Owner] += e.Def.PopCost;
        }
        for (int p = 0; p < 2; p++) { Players[p].Pop = pop[p]; Players[p].PopCap = Math.Min(Defs.MaxPop, cap[p]); }
    }

    internal void Emit(GameEvent t, int owner, Vec2 pos) => Events.Add(new SimEvent(t, owner, pos));

    // ---------------- step ----------------
    private void StepImpl(float dt)
    {
        if (Events.Count > 20000) Events.RemoveRange(0, Events.Count - 2000);
        if (Shots.Count > 20000) Shots.RemoveRange(0, Shots.Count - 2000);
        Time += dt;
        _stepCount++;
        RebuildLists();

        if (AiEnabled && !Winner.HasValue)
            foreach (var ai in _ais) ai.Update(dt);

        for (int i = 0; i < _structs.Count; i++) UpdateStructure(_structs[i], dt);
        for (int i = 0; i < _units.Count; i++) { var u = _units[i]; if (u.Alive) UpdateUnit(u, dt); }
        Separation(dt);
        PushOutOfStructures();
        RemoveDead();
        RecomputePop();
        if (_stepCount % 3 == 0) RecomputeVisibility();
        if (_stepCount % 10 == 0) CheckVictory();
    }

    private void CheckVictory()
    {
        if (Winner.HasValue) return;
        Span<int> count = stackalloc int[2];
        foreach (var e in _entities)
            if (e.Alive && e.Def.IsStructure && e.Owner is 0 or 1) count[e.Owner]++;
        for (int p = 0; p < 2; p++)
        {
            if (count[p] == 0 && !Players[p].Defeated)
            {
                Players[p].Defeated = true;
                int w = 1 - p;
                Winner = w;
                Emit(GameEvent.Defeat, p, _startLoc[p]);
                Emit(GameEvent.Victory, w, _startLoc[w]);
                return;
            }
        }
    }

    // ---------------- picking ----------------
    private Entity? PickAtImpl(Vec2 p)
    {
        for (int i = _entities.Count - 1; i >= 0; i--)
        {
            var e = _entities[i];
            if (!e.Alive || e.Def.IsStructure) continue;
            float r = e.Def.Radius + 3f;
            if (Vec2.DistanceSq(e.Pos, p) <= r * r) return e;
        }
        for (int i = _entities.Count - 1; i >= 0; i--)
        {
            var e = _entities[i];
            if (!e.Alive || !e.Def.IsStructure) continue;
            float h = e.Def.FootprintTiles * TileSize / 2f;
            if (MathF.Abs(p.X - e.Pos.X) <= h && MathF.Abs(p.Y - e.Pos.Y) <= h) return e;
        }
        return null;
    }

    /// <summary>Distance from a point to an entity's surface (structure footprint AABB or unit circle).</summary>
    internal static float SurfDist(Vec2 p, Entity t)
    {
        if (t.Def.IsStructure)
        {
            float h = t.Def.FootprintTiles * TileSize / 2f;
            float dx = MathF.Max(0, MathF.Abs(p.X - t.Pos.X) - h);
            float dy = MathF.Max(0, MathF.Abs(p.Y - t.Pos.Y) - h);
            return MathF.Sqrt(dx * dx + dy * dy);
        }
        return MathF.Max(0, Vec2.Distance(p, t.Pos) - t.Def.Radius);
    }
}
