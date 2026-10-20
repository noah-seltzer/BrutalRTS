using RTS.Core;

namespace RTS.Sim;

public sealed partial class World
{
    private const float InteractDist = 26f;

    // ---------------- commands ----------------
    private IEnumerable<Entity> OwnedUnits(IEnumerable<int> ids)
    {
        var seen = new HashSet<int>();
        foreach (var id in ids)
        {
            if (!seen.Add(id)) continue;
            var e = FindEntity(id);
            if (e != null && e.Alive && !e.Def.IsStructure && e.Owner is 0 or 1) yield return e;
        }
    }

    /// <summary>Cancels the current order (refunds an unplaced build).</summary>
    private void ResetOrder(Entity e)
    {
        var x = e.X;
        RefundUnplacedBuild(e);
        x.BuildSiteId = -1;
        e.Order = UnitOrder.Idle;
        e.TargetId = null;
        e.OrderTarget = null;
        x.Gather = 0;
        ClearPath(e);
    }

    private void CmdMoveImpl(IEnumerable<int> ids, Vec2 target, bool attackMove, bool queue = false)
    {
        var list = OwnedUnits(ids).ToList();
        if (list.Count == 0) return;
        var slots = FormationSlots(list, target);
        var order = attackMove ? UnitOrder.AttackMove : UnitOrder.Move;
        for (int i = 0; i < list.Count; i++) Issue(list[i], new QueuedOrder(order, slots[i]), queue);
    }

    /// <summary>Spread a group around a target point; assigns each unit a slot (greedy by distance).</summary>
    private List<Vec2> FormationSlots(List<Entity> units, Vec2 target)
    {
        var result = new List<Vec2>(new Vec2[units.Count]);
        if (units.Count == 1) { result[0] = target; return result; }
        float maxR = 0; foreach (var u in units) maxR = MathF.Max(maxR, u.Def.Radius);
        float spacing = maxR * 2f + 6f;
        var cand = new List<Vec2>();
        int ring = 0;
        while (cand.Count < units.Count && ring < 30)
        {
            if (ring == 0) { if (FreeAt(target, maxR)) cand.Add(target); }
            else
            {
                int n = (int)(MathF.PI * 2 * ring);
                for (int k = 0; k < n && cand.Count < units.Count * 2; k++)
                {
                    float a = k / (float)n * MathF.PI * 2f + ring * 0.5f;
                    var p = target + new Vec2(MathF.Cos(a), MathF.Sin(a)) * (ring * spacing);
                    if (p.X > 0 && p.Y > 0 && p.X < Width * TileSize && p.Y < Height * TileSize && FreeAt(p, maxR)) cand.Add(p);
                }
            }
            ring++;
        }
        while (cand.Count < units.Count) cand.Add(target);
        // centroid-relative greedy assignment: farthest units pick first
        var used = new bool[cand.Count];
        var order = Enumerable.Range(0, units.Count).OrderByDescending(i => Vec2.DistanceSq(units[i].Pos, target)).ToList();
        int take = Math.Min(cand.Count, units.Count);
        foreach (var i in order)
        {
            int best = -1; float bd = float.MaxValue;
            for (int c = 0; c < take; c++)
            {
                if (used[c]) continue;
                float d = Vec2.DistanceSq(units[i].Pos, cand[c]);
                if (d < bd) { bd = d; best = c; }
            }
            if (best < 0) { result[i] = target; continue; }
            used[best] = true;
            result[i] = cand[best];
        }
        return result;
    }

    private void CmdAttackImpl(IEnumerable<int> ids, int targetId, bool queue = false)
    {
        var t = FindEntity(targetId);
        if (t == null || !t.Alive || t.Owner < 0) return;
        foreach (var e in OwnedUnits(ids))
        {
            if (t.Owner == e.Owner || e.Def.Range <= 0) continue;
            Issue(e, new QueuedOrder(UnitOrder.Attack, TargetId: t.Id), queue);
        }
    }

    private void CmdHarvestImpl(IEnumerable<int> ids, int fieldId, bool queue = false)
    {
        var f = FindEntity(fieldId);
        if (f == null || !f.Alive || f.Kind != EntityKind.CrystalField) return;
        var nonWorkers = new List<int>();
        foreach (var e in OwnedUnits(ids))
        {
            if (e.Kind != EntityKind.Worker) { nonWorkers.Add(e.Id); continue; }
            Issue(e, new QueuedOrder(UnitOrder.Harvest, TargetId: f.Id), queue);
        }
        if (nonWorkers.Count > 0) CmdMoveImpl(nonWorkers, f.Pos, false, queue);
    }

    private void CmdStopImpl(IEnumerable<int> ids) { foreach (var e in OwnedUnits(ids)) { ClearQueue(e); ResetOrder(e); } }

    private void CmdHoldImpl(IEnumerable<int> ids)
    {
        foreach (var e in OwnedUnits(ids)) { ClearQueue(e); ResetOrder(e); e.Order = UnitOrder.Hold; e.OrderTarget = e.Pos; }
    }

    private void CmdSmartImpl(IEnumerable<int> ids, Vec2 target, int? targetEntityId, bool queue = false)
    {
        var list = OwnedUnits(ids).ToList();
        if (list.Count == 0) return;
        var t = targetEntityId.HasValue ? FindEntity(targetEntityId.Value) : null;
        if (t != null && t.Alive)
        {
            int owner = list[0].Owner;
            if (t.Owner >= 0 && t.Owner != owner)
            {
                var attackers = list.Where(u => u.Def.Range > 0).Select(u => u.Id).ToList();
                CmdAttackImpl(attackers, t.Id, queue);
                var rest = list.Where(u => u.Def.Range <= 0).Select(u => u.Id).ToList();
                if (rest.Count > 0) CmdMoveImpl(rest, target, false, queue);
                return;
            }
            if (t.Kind == EntityKind.CrystalField)
            {
                CmdHarvestImpl(list.Select(u => u.Id), t.Id, queue);
                return;
            }
            if (t.Owner == owner && t.Def.IsStructure)
            {
                var workers = list.Where(u => u.Kind == EntityKind.Worker).ToList();
                var others = list.Where(u => u.Kind != EntityKind.Worker).Select(u => u.Id).ToList();
                foreach (var w in workers)
                {
                    if (t.UnderConstruction)
                        Issue(w, new QueuedOrder(UnitOrder.Build, t.Pos, t.Id, t.Kind, t.X.TileX, t.X.TileY), queue);
                    else if (t.Kind == EntityKind.HQ && w.Cargo > 0)
                        Issue(w, new QueuedOrder(UnitOrder.ReturnCargo, TargetId: t.Id), queue);
                    else others.Add(w.Id);
                }
                if (others.Count > 0) CmdMoveImpl(others, target, false, queue);
                return;
            }
        }
        CmdMoveImpl(list.Select(u => u.Id), target, false, queue);
    }

    // ---------------- per-unit update ----------------
    private void UpdateUnit(Entity e, float dt)
    {
        var x = e.X;
        if (e.AttackCooldownLeft > 0) e.AttackCooldownLeft -= dt;
        x.RepathTimer -= dt;
        x.ScanTimer -= dt;
        if (e.Order == UnitOrder.Idle && x.Queue.Count > 0) StartNextQueued(e);

        switch (e.Order)
        {
            case UnitOrder.Idle:
                if (e.Def.Range > 0 && e.Kind != EntityKind.Worker && x.ScanTimer <= 0)
                {
                    x.ScanTimer = 0.3f;
                    var t = FindEnemy(e, e.Def.Sight);
                    if (t != null)
                    {
                        e.Order = UnitOrder.AttackMove;
                        e.OrderTarget = e.Pos; // return here afterwards
                        e.TargetId = t.Id;
                    }
                }
                break;
            case UnitOrder.Hold:
                {
                    Entity? t = e.TargetId.HasValue ? FindEntity(e.TargetId.Value) : null;
                    if (t == null || !t.Alive || !InRange(e, t)) { t = null; e.TargetId = null; }
                    if (t == null && e.Def.Range > 0 && x.ScanTimer <= 0)
                    {
                        x.ScanTimer = 0.25f;
                        t = FindEnemy(e, e.Def.Range + e.Def.Radius);
                        e.TargetId = t?.Id;
                    }
                    if (t != null) TryFire(e, t);
                    break;
                }
            case UnitOrder.Move:
                if (!FollowPath(e, dt)) { ClearPath(e); e.Order = UnitOrder.Idle; e.OrderTarget = null; }
                break;
            case UnitOrder.AttackMove: UpdateAttackMove(e, dt); break;
            case UnitOrder.Attack: UpdateAttack(e, dt); break;
            case UnitOrder.Harvest: UpdateHarvest(e, dt); break;
            case UnitOrder.ReturnCargo: UpdateReturn(e, dt); break;
            case UnitOrder.Build: UpdateBuild(e, dt); break;
        }

        // stuck detection
        if (HasPath(e))
        {
            x.StuckTimer += dt;
            if (x.StuckTimer >= 1f)
            {
                if (Vec2.DistanceSq(e.Pos, x.StuckPos) < 6f * 6f)
                {
                    var final = x.Path![^1];
                    if (e.Order == UnitOrder.Move && Vec2.Distance(e.Pos, final) < 64f) { ClearPath(e); e.Order = UnitOrder.Idle; }
                    else if (e.Order is UnitOrder.Move or UnitOrder.AttackMove) { x.Path = FindPath(e.Pos, final, e.Def.Radius); x.PathIdx = 0; }
                    else { ClearPath(e); x.RepathTimer = 0; }
                }
                x.StuckTimer = 0; x.StuckPos = e.Pos;
            }
        }
        else { x.StuckTimer = 0; x.StuckPos = e.Pos; }
    }

    private void UpdateAttackMove(Entity e, float dt)
    {
        var x = e.X;
        Entity? t = e.TargetId.HasValue ? FindEntity(e.TargetId.Value) : null;
        if (t != null && (!t.Alive || SurfDist(e.Pos, t) > e.Def.Sight + 64f)) { t = null; e.TargetId = null; if (e.OrderTarget.HasValue) SetPath(e, e.OrderTarget.Value); }
        if (t == null && e.Def.Range > 0 && x.ScanTimer <= 0)
        {
            x.ScanTimer = 0.3f;
            t = FindEnemy(e, e.Def.Sight);
            if (t != null) { e.TargetId = t.Id; ClearPath(e); x.RepathTimer = 0; }
        }
        if (t != null)
        {
            if (!TryFire(e, t)) Approach(e, t, dt);
            else ClearPath(e);
            return;
        }
        if (!HasPath(e))
        {
            if (e.OrderTarget.HasValue && Vec2.Distance(e.Pos, e.OrderTarget.Value) > 40f && x.RepathTimer <= 0)
            {
                SetPath(e, e.OrderTarget.Value);
                x.RepathTimer = 1.5f;
                if (!HasPath(e)) { e.Order = UnitOrder.Idle; e.OrderTarget = null; }
            }
            else if (!e.OrderTarget.HasValue || Vec2.Distance(e.Pos, e.OrderTarget.Value) <= 40f) { e.Order = UnitOrder.Idle; e.OrderTarget = null; }
            return;
        }
        FollowPath(e, dt);
    }

    private void UpdateAttack(Entity e, float dt)
    {
        Entity? t = e.TargetId.HasValue ? FindEntity(e.TargetId.Value) : null;
        if (t == null || !t.Alive) { ResetOrder(e); return; }
        if (TryFire(e, t)) { ClearPath(e); return; }
        Approach(e, t, dt);
    }

    internal bool InRange(Entity e, Entity t) => SurfDist(e.Pos, t) - e.Def.Radius <= e.Def.Range;

    /// <summary>Fires at t if in range (respecting cooldown). Returns true if in range.</summary>
    private bool TryFire(Entity e, Entity t)
    {
        if (e.Def.Range <= 0 || !InRange(e, t)) return false;
        var d = t.Pos - e.Pos;
        if (d.LengthSq > 0.01f) e.Facing = d.Normalized;
        if (e.AttackCooldownLeft <= 0)
        {
            e.AttackCooldownLeft = e.Def.Cooldown;
            if (e.Def.Range >= 60f)
            {
                Emit(GameEvent.Shot, e.Owner, e.Pos);
                Shots.Add(new ShotFx(e.Pos, t.Pos, e.Owner, e.Kind));
            }
            DealDamage(t, HitDamage(e, t), e);
        }
        return true;
    }

    internal void DealDamage(Entity t, int amount, Entity? src)
    {
        if (!t.Alive) return;
        t.Hp -= amount;
        t.LastHitTime = Time;
        if (t.Owner is 0 or 1 && Time - _lastUnderAttack[t.Owner] >= 8f)
        {
            _lastUnderAttack[t.Owner] = Time;
            Emit(GameEvent.UnderAttack, t.Owner, t.Pos);
        }
        foreach (var ai in _ais) if (ai.Me == t.Owner) ai.NotifyAttacked(t, src);
        if (t.Hp <= 0)
        {
            t.Hp = 0;
            Kill(t);
            if (src != null && src.Alive)
            {
                src.Kills++;
                int rank = src.Kills >= 12 ? 3 : src.Kills >= 7 ? 2 : src.Kills >= 3 ? 1 : 0;
                if (rank > src.Rank) src.Rank = rank; // cosmetic: no stat change, no heal
            }
            return;
        }
        // retaliation
        if (src != null && src.Alive && !t.Def.IsStructure && t.Def.Range > 0 && t.Order == UnitOrder.Idle)
        {
            t.Order = UnitOrder.AttackMove;
            t.OrderTarget = t.Pos;
            t.TargetId = src.Id;
        }
    }

    private void Kill(Entity t)
    {
        Emit(GameEvent.Explosion, t.Owner, t.Pos);
        if (!t.Def.IsStructure && t.Owner is 0 or 1) Emit(GameEvent.UnitLost, t.Owner, t.Pos);
        RefundUnplacedBuild(t);
        ClearQueue(t);
        ClearPath(t);
    }

    /// <summary>Best enemy target within radius (prefers armed units, then structures, then workers).</summary>
    internal Entity? FindEnemy(Entity e, float radius)
    {
        Entity? best = null; float bestScore = float.MaxValue;
        foreach (var u in QueryUnits(e.Pos, radius))
        {
            if (!u.Alive || u.Owner < 0 || u.Owner == e.Owner) continue;
            float d = MathF.Max(0, Vec2.Distance(e.Pos, u.Pos) - u.Def.Radius);
            if (d > radius) continue;
            float score = d + (u.Kind == EntityKind.Worker ? 60f : 0f);
            if (score < bestScore) { bestScore = score; best = u; }
        }
        foreach (var s in _structs)
        {
            if (!s.Alive || s.Owner < 0 || s.Owner == e.Owner) continue;
            float d = SurfDist(e.Pos, s);
            if (d > radius) continue;
            float score = d + (s.Def.Range > 0 && !s.UnderConstruction ? 10f : 100f);
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    // ---------------- harvesting ----------------
    internal Entity? NearestField(Vec2 p, float maxDist)
    {
        Entity? best = null; float bd = maxDist * maxDist;
        foreach (var f in _entities)
        {
            if (f.Kind != EntityKind.CrystalField || !f.Alive || f.Resources <= 0) continue;
            float d = Vec2.DistanceSq(p, f.Pos);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    internal Entity? NearestDropoff(int owner, Vec2 p)
    {
        Entity? best = null; float bd = float.MaxValue;
        foreach (var s in _structs)
        {
            if (s.Owner != owner || s.Kind != EntityKind.HQ || s.UnderConstruction || !s.Alive) continue;
            float d = Vec2.DistanceSq(p, s.Pos);
            if (d < bd) { bd = d; best = s; }
        }
        return best;
    }

    private void UpdateHarvest(Entity e, float dt)
    {
        var x = e.X;
        if (e.Cargo >= Defs.HarvestPerTrip) { e.Order = UnitOrder.ReturnCargo; ClearPath(e); return; }
        Entity? f = e.TargetId.HasValue ? FindEntity(e.TargetId.Value) : null;
        if (f == null || !f.Alive || f.Resources <= 0)
        {
            var refPos = f?.Pos ?? e.Pos;
            var nf = NearestField(refPos, 15 * TileSize) ?? NearestField(e.Pos, 25 * TileSize);
            if (nf == null) { if (e.Cargo > 0) e.Order = UnitOrder.ReturnCargo; else ResetOrder(e); return; }
            e.TargetId = nf.Id; x.LastField = nf.Id; ClearPath(e); x.RepathTimer = 0; x.Gather = 0;
            return;
        }
        if (SurfDist(e.Pos, f) - e.Def.Radius <= InteractDist - 8f || (SurfDist(e.Pos, f) <= InteractDist && !HasPath(e)))
        {
            ClearPath(e);
            var d = f.Pos - e.Pos; if (d.LengthSq > 0.01f) e.Facing = d.Normalized;
            x.Gather += dt;
            if (x.Gather >= Defs.HarvestTime)
            {
                x.Gather = 0;
                int take = Math.Min(Defs.HarvestPerTrip - e.Cargo, f.Resources);
                f.Resources -= take;
                e.Cargo += take;
                x.LastField = f.Id;
                if (f.Resources <= 0) { f.Hp = 0; }
                e.Order = UnitOrder.ReturnCargo;
                x.RepathTimer = 0;
            }
            return;
        }
        x.Gather = 0;
        Approach(e, f, dt);
    }

    private void UpdateReturn(Entity e, float dt)
    {
        var x = e.X;
        if (e.Cargo <= 0) { e.Order = UnitOrder.Harvest; e.TargetId = x.LastField >= 0 ? x.LastField : null; ClearPath(e); x.RepathTimer = 0; return; }
        var hq = NearestDropoff(e.Owner, e.Pos);
        if (hq == null) { if (x.Queue.Count > 0) ResetOrder(e); else ClearPath(e); return; } // wait with cargo
        if (SurfDist(e.Pos, hq) - e.Def.Radius <= InteractDist - 8f || (SurfDist(e.Pos, hq) <= InteractDist && !HasPath(e)))
        {
            int amount = e.Cargo;
            foreach (var ai in _ais) if (ai.Me == e.Owner) amount += ai.IncomeBonus(amount);
            Players[e.Owner].Credits += amount;
            e.Cargo = 0;
            ClearPath(e);
            if (x.Queue.Count > 0) { ResetOrder(e); return; } // a harvest trip ends here when orders are queued
            e.Order = UnitOrder.Harvest;
            var lf = x.LastField >= 0 ? FindEntity(x.LastField) : null;
            e.TargetId = lf != null && lf.Alive ? lf.Id : NearestField(e.Pos, 25 * TileSize)?.Id;
            if (e.TargetId == null) e.Order = UnitOrder.Idle;
            x.RepathTimer = 0;
            return;
        }
        Approach(e, hq, dt);
    }

    // ---------------- building ----------------
    private void UpdateBuild(Entity e, float dt)
    {
        var x = e.X;
        var d = Defs.Get(x.BuildKind);
        int s = d.FootprintTiles;
        if (x.BuildSiteId < 0)
        {
            var center = FootprintCenter(x.BuildKind, x.BuildTx, x.BuildTy);
            float h = s * TileSize / 2f;
            float dx = MathF.Max(0, MathF.Abs(e.Pos.X - center.X) - h), dy = MathF.Max(0, MathF.Abs(e.Pos.Y - center.Y) - h);
            float sd = MathF.Sqrt(dx * dx + dy * dy);
            if (sd <= InteractDist || (!HasPath(e) && sd <= InteractDist + 16f))
            {
                if (!FootprintClear(x.BuildKind, x.BuildTx, x.BuildTy) || !MeetsRequirement(e.Owner, x.BuildKind)) { ResetOrder(e); return; }
                var site = SpawnStructure(x.BuildKind, e.Owner, x.BuildTx, x.BuildTy, complete: false);
                x.BuildSiteId = site.Id;
                x.BuildPaid = false;
                ClearPath(e);
                return;
            }
            if (!HasPath(e) && x.RepathTimer <= 0)
            {
                e.X.Path = FindPath(e.Pos, center, e.Def.Radius, x.BuildTx, x.BuildTy, s);
                e.X.PathIdx = 0;
                x.RepathTimer = 1f;
            }
            if (!FollowPath(e, dt)) MoveStraight(e, center, dt);
            return;
        }
        var siteE = FindEntity(x.BuildSiteId);
        if (siteE == null || !siteE.Alive || !siteE.UnderConstruction) { ResetOrder(e); return; }
        if (SurfDist(e.Pos, siteE) <= InteractDist + 4f)
        {
            ClearPath(e);
            var fd = siteE.Pos - e.Pos; if (fd.LengthSq > 0.01f) e.Facing = fd.Normalized;
            float dp = dt / d.BuildTime;
            float before = siteE.BuildProgress;
            siteE.BuildProgress = MathF.Min(1f, before + dp);
            siteE.Hp = Math.Min(MaxHpOf(siteE), siteE.Hp + (int)MathF.Ceiling(d.MaxHp * 0.9f * (siteE.BuildProgress - before)));
            if (siteE.BuildProgress >= 1f && !siteE.X.CompletionHandled)
            {
                siteE.X.CompletionHandled = true;
                Emit(GameEvent.StructureComplete, siteE.Owner, siteE.Pos);
                ResetOrder(e);
            }
            return;
        }
        Approach(e, siteE, dt);
    }

    /// <summary>Terrain + structure check only (units are pushed aside).</summary>
    internal bool FootprintClear(EntityKind k, int tx, int ty)
    {
        int s = Defs.Get(k).FootprintTiles;
        if (tx < 0 || ty < 0 || tx + s > Width || ty + s > Height) return false;
        for (int x = tx; x < tx + s; x++)
            for (int y = ty; y < ty + s; y++)
                if (_terrain[x, y] != Terrain.Grass || _occ[y * Width + x] != 0) return false;
        return true;
    }

    /// <summary>ignoreQueueOf: a worker whose queued placements don't count (a non-Shift build clears them).</summary>
    private bool OverlapsPendingSite(int owner, EntityKind k, int tx, int ty, int margin = 0, Entity? ignoreQueueOf = null)
    {
        int s = Defs.Get(k).FootprintTiles;
        bool Overlaps(EntityKind otherKind, int ox, int oy)
        {
            int otherSize = Defs.Get(otherKind).FootprintTiles;
            return tx - margin < ox + otherSize && ox < tx + s + margin && ty - margin < oy + otherSize && oy < ty + s + margin;
        }
        foreach (var u in _entities)
        {
            if (u.Owner != owner || !u.Alive) continue;
            if (u.Order == UnitOrder.Build && u.X.BuildSiteId < 0 && Overlaps(u.X.BuildKind, u.X.BuildTx, u.X.BuildTy)) return true;
            if (u == ignoreQueueOf) continue;
            foreach (var q in u.X.Queue)
                if (IsPlacement(q) && Overlaps(q.BuildKind, q.BuildTx, q.BuildTy)) return true;
        }
        return false;
    }

    private bool CanPlaceImpl(int owner, EntityKind structure, int tileX, int tileY, Entity? ignoreQueueOf = null)
    {
        var d = Defs.Get(structure);
        if (!d.IsStructure || structure == EntityKind.CrystalField || owner is not (0 or 1)) return false;
        if (!FootprintClear(structure, tileX, tileY)) return false;
        if (!MeetsRequirement(owner, structure)) return false;
        if (OverlapsPendingSite(owner, structure, tileX, tileY, ignoreQueueOf: ignoreQueueOf)) return false;
        if (owner == 0 || !_ais.Any(a => a.Me == owner))
        {
            int s = d.FootprintTiles;
            for (int x = tileX; x < tileX + s; x++)
                for (int y = tileY; y < tileY + s; y++)
                    if (_vis[owner][y * Width + x] == 0) return false;
        }
        // no enemy units on the footprint
        var c = FootprintCenter(structure, tileX, tileY);
        foreach (var u in QueryUnits(c, d.FootprintTiles * TileSize * 0.5f))
            if (u.Owner != owner && u.Owner >= 0 && MathF.Abs(u.Pos.X - c.X) < d.FootprintTiles * TileSize / 2f && MathF.Abs(u.Pos.Y - c.Y) < d.FootprintTiles * TileSize / 2f) return false;
        return true;
    }

    private bool CmdBuildImpl(int workerId, EntityKind structure, int tileX, int tileY, bool queue = false)
    {
        var w = FindEntity(workerId);
        if (w == null || !w.Alive || w.Owner is not (0 or 1) || !w.Def.Builds.Contains(structure)) return false;
        if (!CanPlaceImpl(w.Owner, structure, tileX, tileY, ignoreQueueOf: queue ? null : w)) return false;
        var p = Players[w.Owner];
        int cost = Defs.Get(structure).Cost;
        if (p.Credits < cost) { Emit(GameEvent.InsufficientCredits, w.Owner, w.Pos); return false; }
        p.Credits -= cost; // queued builds are paid up front; ClearQueue/ResetOrder refund unplaced ones
        Issue(w, new QueuedOrder(UnitOrder.Build, FootprintCenter(structure, tileX, tileY), null, structure, tileX, tileY), queue);
        return true;
    }
}
