using RTS.Core;

namespace RTS.Sim;

public sealed partial class World
{
    private void UpdateStructure(Entity s, float dt)
    {
        if (s.UnderConstruction || s.Owner is not (0 or 1)) return;
        // turrets
        if (s.Def.Range > 0)
        {
            if (s.AttackCooldownLeft > 0) s.AttackCooldownLeft -= dt;
            Entity? t = s.TargetId.HasValue ? FindEntity(s.TargetId.Value) : null;
            if (t == null || !t.Alive || !StructInRange(s, t))
            {
                t = null; s.TargetId = null;
                s.X.ScanTimer -= dt;
                if (s.X.ScanTimer <= 0)
                {
                    s.X.ScanTimer = 0.25f;
                    t = FindEnemyForStructure(s);
                    s.TargetId = t?.Id;
                }
            }
            if (t != null && s.AttackCooldownLeft <= 0)
            {
                s.AttackCooldownLeft = s.Def.Cooldown;
                s.Facing = (t.Pos - s.Pos).Normalized;
                Emit(GameEvent.Shot, s.Owner, s.Pos);
                Shots.Add(new ShotFx(s.Pos, t.Pos, s.Owner, s.Kind));
                DealDamage(t, HitDamage(s, t), s);
            }
        }
        // production
        if (s.TrainQueue.Count == 0) return;
        var k = s.TrainQueue[0];
        var d = Defs.Get(k);
        var p = Players[s.Owner];
        if (!s.X.TrainStarted)
        {
            if (p.Pop + d.PopCost > p.PopCap)
            {
                if (Time - _lastPopCapped[s.Owner] >= 5f) { _lastPopCapped[s.Owner] = Time; Emit(GameEvent.PopCapped, s.Owner, s.Pos); }
                return;
            }
            s.X.TrainStarted = true;
            p.Pop += d.PopCost;
        }
        s.TrainProgress += dt;
        if (s.TrainProgress >= d.BuildTime)
        {
            s.TrainQueue.RemoveAt(0);
            s.TrainProgress = 0;
            s.X.TrainStarted = false;
            SpawnFromStructure(s, k);
        }
    }

    private bool StructInRange(Entity s, Entity t) => SurfDist(s.Pos, t) - s.Def.FootprintTiles * TileSize / 2f <= s.Def.Range;

    private Entity? FindEnemyForStructure(Entity s)
    {
        float reach = s.Def.Range + s.Def.FootprintTiles * TileSize / 2f;
        Entity? best = null; float bd = float.MaxValue;
        foreach (var u in QueryUnits(s.Pos, reach))
        {
            if (!u.Alive || u.Owner < 0 || u.Owner == s.Owner) continue;
            float d = Vec2.Distance(s.Pos, u.Pos) + (u.Kind == EntityKind.Worker ? 40f : 0f);
            if (d < bd && StructInRange(s, u)) { bd = d; best = u; }
        }
        if (best != null) return best;
        foreach (var o in _structs)
        {
            if (!o.Alive || o.Owner < 0 || o.Owner == s.Owner || !StructInRange(s, o)) continue;
            float d = Vec2.Distance(s.Pos, o.Pos);
            if (d < bd) { bd = d; best = o; }
        }
        return best;
    }

    private void SpawnFromStructure(Entity s, EntityKind k)
    {
        Vec2 toward = s.RallyPoint ?? new Vec2(Width * TileSize / 2f, Height * TileSize / 2f);
        int fs = s.Def.FootprintTiles;
        Vec2 spawn = s.Pos;
        float bd = float.MaxValue;
        for (int ring = 1; ring <= 5 && bd == float.MaxValue; ring++)
            for (int x = s.X.TileX - ring; x < s.X.TileX + fs + ring; x++)
                for (int y = s.X.TileY - ring; y < s.X.TileY + fs + ring; y++)
                {
                    bool edge = x == s.X.TileX - ring || y == s.X.TileY - ring || x == s.X.TileX + fs + ring - 1 || y == s.X.TileY + fs + ring - 1;
                    if (!edge || !PassIdx(x, y)) continue;
                    var c = TileCenter(x, y);
                    float d = Vec2.DistanceSq(c, toward) + QueryUnits(c, 8f).Count * 2000f;
                    if (d < bd) { bd = d; spawn = c; }
                }
        var u = SpawnUnit(k, s.Owner, spawn);
        Emit(GameEvent.UnitTrained, s.Owner, spawn);
        if (s.RallyPoint is Vec2 rp)
        {
            var at = PickAtImpl(rp);
            if (u.Kind == EntityKind.Worker && at != null && at.Kind == EntityKind.CrystalField)
            {
                u.Order = UnitOrder.Harvest; u.TargetId = at.Id; u.X.LastField = at.Id;
            }
            else
            {
                var off = new Vec2((float)(_rng.NextDouble() - 0.5) * 40f, (float)(_rng.NextDouble() - 0.5) * 40f);
                var goal = rp + off;
                if (!FreeAt(goal, u.Def.Radius)) goal = rp;
                u.Order = UnitOrder.Move;
                u.OrderTarget = goal;
                SetPath(u, goal);
            }
        }
    }

    private bool CmdTrainImpl(int structureId, EntityKind unit)
    {
        var s = FindEntity(structureId);
        if (s == null || !s.Alive || s.Owner is not (0 or 1) || s.UnderConstruction || !s.Def.Trains.Contains(unit)) return false;
        if (s.TrainQueue.Count >= Defs.MaxQueue) return false;
        if (!MeetsRequirement(s.Owner, unit)) return false;
        var p = Players[s.Owner];
        int cost = Defs.Get(unit).Cost;
        if (p.Credits < cost) { Emit(GameEvent.InsufficientCredits, s.Owner, s.Pos); return false; }
        p.Credits -= cost;
        s.TrainQueue.Add(unit);
        return true;
    }

    private void CmdCancelTrainImpl(int structureId)
    {
        var s = FindEntity(structureId);
        if (s == null || !s.Alive || s.Owner is not (0 or 1) || s.TrainQueue.Count == 0) return;
        int idx = s.TrainQueue.Count - 1;
        var k = s.TrainQueue[idx];
        s.TrainQueue.RemoveAt(idx);
        Players[s.Owner].Credits += Defs.Get(k).Cost;
        if (idx == 0)
        {
            if (s.X.TrainStarted) Players[s.Owner].Pop -= Defs.Get(k).PopCost;
            s.TrainProgress = 0;
            s.X.TrainStarted = false;
        }
    }

    private void CmdRallyImpl(int structureId, Vec2 point)
    {
        var s = FindEntity(structureId);
        if (s == null || !s.Alive || s.Owner is not (0 or 1) || !s.Def.IsStructure) return;
        s.RallyPoint = point;
    }

    // ---------------- fog of war ----------------
    private byte[][] _vis = null!;
    private readonly Dictionary<int, (int dx, int dy)[]> _circleCache = new();

    private void InitFog() { _vis = new[] { new byte[Width * Height], new byte[Width * Height] }; }

    private byte VisibilityImpl(int player, int tx, int ty)
    {
        if (player is not (0 or 1) || !InBounds(tx, ty)) return 0;
        return _vis[player][ty * Width + tx];
    }

    private (int dx, int dy)[] Circle(float sight)
    {
        int key = (int)MathF.Round(sight);
        if (_circleCache.TryGetValue(key, out var c)) return c;
        float r = sight / TileSize;
        int ri = (int)MathF.Ceiling(r);
        var l = new List<(int, int)>();
        for (int dx = -ri; dx <= ri; dx++) for (int dy = -ri; dy <= ri; dy++) if (dx * dx + dy * dy <= r * r) l.Add((dx, dy));
        c = l.ToArray();
        _circleCache[key] = c;
        return c;
    }

    private void RecomputeVisibility()
    {
        int n = Width * Height;
        for (int p = 0; p < 2; p++)
        {
            var v = _vis[p];
            for (int i = 0; i < n; i++) if (v[i] == 2) v[i] = 1;
        }
        foreach (var e in _entities)
        {
            if (!e.Alive || e.Owner is not (0 or 1) || e.Def.Sight <= 0) continue;
            var v = _vis[e.Owner];
            var (tx, ty) = WorldToTile(e.Pos);
            foreach (var (dx, dy) in Circle(e.Def.Sight))
            {
                int x = tx + dx, y = ty + dy;
                if ((uint)x < (uint)Width && (uint)y < (uint)Height) v[y * Width + x] = 2;
            }
        }
    }

    /// <summary>True if the entity is currently visible to the player (own entities always visible).</summary>
    public bool IsVisibleTo(int player, Entity e)
    {
        if (e.Owner == player) return true;
        var (tx, ty) = WorldToTile(e.Pos);
        return VisibilityImpl(player, tx, ty) == 2;
    }
}
