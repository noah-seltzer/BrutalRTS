using RTS.Core;

namespace RTS.Sim;

public sealed partial class World
{
    private float[] _g = null!;
    private int[] _par = null!;
    private int[] _seen = null!;
    private int[] _closed = null!;
    private int _stamp;
    private readonly PriorityQueue<int, float> _open = new();
    private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
    private const float Sqrt2 = 1.41421356f;

    // spatial grid of units
    private const float CellSize = 64f;
    private int _gw, _gh;
    private List<Entity>[] _cells = null!;
    private readonly List<Entity> _query = new();

    private void InitPathfinding()
    {
        int n = Width * Height;
        _g = new float[n]; _par = new int[n]; _seen = new int[n]; _closed = new int[n];
        _gw = (int)MathF.Ceiling(Width * TileSize / CellSize);
        _gh = (int)MathF.Ceiling(Height * TileSize / CellSize);
        _cells = new List<Entity>[_gw * _gh];
        for (int i = 0; i < _cells.Length; i++) _cells[i] = new List<Entity>(4);
    }

    private void BuildSpatialGrid()
    {
        foreach (var c in _cells) c.Clear();
        foreach (var u in _units)
        {
            int cx = Math.Clamp((int)(u.Pos.X / CellSize), 0, _gw - 1), cy = Math.Clamp((int)(u.Pos.Y / CellSize), 0, _gh - 1);
            _cells[cy * _gw + cx].Add(u);
        }
    }

    /// <summary>Units whose center is within radius (+their radius) of p. Returns shared buffer.</summary>
    internal List<Entity> QueryUnits(Vec2 p, float radius)
    {
        _query.Clear();
        int x0 = Math.Clamp((int)((p.X - radius - 16) / CellSize), 0, _gw - 1), x1 = Math.Clamp((int)((p.X + radius + 16) / CellSize), 0, _gw - 1);
        int y0 = Math.Clamp((int)((p.Y - radius - 16) / CellSize), 0, _gh - 1), y1 = Math.Clamp((int)((p.Y + radius + 16) / CellSize), 0, _gh - 1);
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
                foreach (var u in _cells[cy * _gw + cx])
                {
                    float r = radius + u.Def.Radius;
                    if (Vec2.DistanceSq(u.Pos, p) <= r * r) _query.Add(u);
                }
        return _query;
    }

    private bool PassIdx(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && _terrain[x, y] == Terrain.Grass && _occ[y * Width + x] == 0;

    internal bool FreeAt(Vec2 p, float r)
    {
        float k = r * 0.85f;
        return PassPt(p.X - k, p.Y - k) && PassPt(p.X + k, p.Y - k) && PassPt(p.X - k, p.Y + k) && PassPt(p.X + k, p.Y + k) && PassPt(p.X, p.Y);
    }
    private bool PassPt(float x, float y) => PassIdx((int)MathF.Floor(x / TileSize), (int)MathF.Floor(y / TileSize));

    internal bool LineClear(Vec2 a, Vec2 b, float r)
    {
        float len = Vec2.Distance(a, b);
        int steps = Math.Max(1, (int)(len / 8f));
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            if (!FreeAt(new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t), r)) return false;
        }
        return true;
    }

    /// <summary>Nearest passable tile to (tx,ty) by ring search.</summary>
    internal (int x, int y)? NearestPassable(int tx, int ty, int maxR = 12)
    {
        if (PassIdx(tx, ty)) return (tx, ty);
        for (int r = 1; r <= maxR; r++)
        {
            (int, int)? best = null; float bd = float.MaxValue;
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (!PassIdx(tx + dx, ty + dy)) continue;
                    float d = dx * dx + dy * dy;
                    if (d < bd) { bd = d; best = (tx + dx, ty + dy); }
                }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>
    /// A* path from 'from' to 'to'. If rect (rx,ry,rs) given, goal = any passable tile adjacent to the rect (approach a structure).
    /// Returns smoothed world waypoints (excluding start). Falls back to the closest reachable tile.
    /// </summary>
    internal List<Vec2> FindPath(Vec2 from, Vec2 to, float radius, int rx = 0, int ry = 0, int rs = 0)
    {
        var (sx, sy) = WorldToTile(from);
        sx = Math.Clamp(sx, 0, Width - 1); sy = Math.Clamp(sy, 0, Height - 1);
        bool rectMode = rs > 0;
        int gx, gy;
        Vec2 finalPt = to;
        if (rectMode)
        {
            // approach point: closest point on rect pushed outward
            float x0 = rx * TileSize, y0 = ry * TileSize, x1 = (rx + rs) * TileSize, y1 = (ry + rs) * TileSize;
            var c = new Vec2(Math.Clamp(from.X, x0, x1), Math.Clamp(from.Y, y0, y1));
            var outDir = (from - c).Normalized;
            if (outDir.LengthSq < 0.5f) outDir = (from - new Vec2((x0 + x1) / 2, (y0 + y1) / 2)).Normalized;
            finalPt = c + outDir * (radius + 4f);
            if (FreeAt(finalPt, radius) && LineClear(from, finalPt, radius)) return new List<Vec2> { finalPt };
            gx = rx + rs / 2; gy = ry + rs / 2;
        }
        else
        {
            (gx, gy) = WorldToTile(to);
            gx = Math.Clamp(gx, 0, Width - 1); gy = Math.Clamp(gy, 0, Height - 1);
            if (!PassIdx(gx, gy))
            {
                var np = NearestPassable(gx, gy, 20);
                if (np == null) return new List<Vec2>();
                (gx, gy) = np.Value;
                finalPt = TileCenter(gx, gy);
            }
            if (FreeAt(finalPt, radius) && LineClear(from, finalPt, radius)) return new List<Vec2> { finalPt };
        }

        _stamp++;
        _open.Clear();
        int W = Width;
        int start = sy * W + sx;
        _g[start] = 0; _par[start] = -1; _seen[start] = _stamp;
        _open.Enqueue(start, H(sx, sy));
        int bestNode = start; float bestH = H(sx, sy);
        int goalNode = -1;
        int expansions = 0;
        while (_open.TryDequeue(out int cur, out _))
        {
            if (_closed[cur] == _stamp) continue;
            _closed[cur] = _stamp;
            int cx = cur % W, cy = cur / W;
            if (IsGoal(cx, cy) && (cur != start || !rectMode || PassIdx(cx, cy))) { goalNode = cur; break; }
            float hc = H(cx, cy);
            if (hc < bestH) { bestH = hc; bestNode = cur; }
            if (++expansions > W * Height) break;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + DX[d], ny = cy + DY[d];
                if (!PassIdx(nx, ny)) continue;
                if (d >= 4 && (!PassIdx(cx + DX[d], cy) || !PassIdx(cx, cy + DY[d]))) continue; // no corner cutting
                int ni = ny * W + nx;
                if (_closed[ni] == _stamp) continue;
                float ng = _g[cur] + (d >= 4 ? Sqrt2 : 1f);
                if (_seen[ni] != _stamp || ng < _g[ni])
                {
                    _seen[ni] = _stamp; _g[ni] = ng; _par[ni] = cur;
                    _open.Enqueue(ni, ng + H(nx, ny));
                }
            }
        }
        bool reached = goalNode >= 0;
        int end = reached ? goalNode : bestNode;
        var tiles = new List<int>();
        for (int n = end; n != -1; n = _par[n]) tiles.Add(n);
        tiles.Reverse();
        var pts = new List<Vec2>(tiles.Count);
        for (int i = 1; i < tiles.Count; i++) pts.Add(TileCenter(tiles[i] % W, tiles[i] / W));
        if (reached)
        {
            if (rectMode)
            {
                // try to finish closer to the structure surface
                var last = pts.Count > 0 ? pts[^1] : from;
                if (FreeAt(finalPt, radius) && LineClear(last, finalPt, radius)) pts.Add(finalPt);
            }
            else
            {
                if (pts.Count > 0) pts[^1] = finalPt; else pts.Add(finalPt);
            }
        }
        return Smooth(from, pts, radius);

        bool IsGoal(int x, int y)
        {
            if (!rectMode) return x == gx && y == gy;
            bool inside = x >= rx && x < rx + rs && y >= ry && y < ry + rs;
            return !inside && x >= rx - 1 && x <= rx + rs && y >= ry - 1 && y <= ry + rs;
        }
        float H(int x, int y)
        {
            int dx, dy;
            if (rectMode)
            {
                dx = Math.Max(0, Math.Max(rx - 1 - x, x - (rx + rs)));
                dy = Math.Max(0, Math.Max(ry - 1 - y, y - (ry + rs)));
            }
            else { dx = Math.Abs(x - gx); dy = Math.Abs(y - gy); }
            int mn = Math.Min(dx, dy), mx = Math.Max(dx, dy);
            return mx - mn + mn * Sqrt2;
        }
    }

    private List<Vec2> Smooth(Vec2 from, List<Vec2> pts, float r)
    {
        if (pts.Count <= 1) return pts;
        var res = new List<Vec2>();
        var cur = from;
        int i = 0;
        while (i < pts.Count)
        {
            int far = i;
            for (int k = i + 1; k < pts.Count; k++)
            {
                if (LineClear(cur, pts[k], r)) far = k;
                else break;
            }
            res.Add(pts[far]);
            cur = pts[far];
            i = far + 1;
        }
        return res;
    }

    // ---------------- movement primitives ----------------
    internal void TryMove(Entity e, Vec2 np)
    {
        float r = e.Def.Radius;
        float maxX = Width * TileSize - 1, maxY = Height * TileSize - 1;
        np = new Vec2(Math.Clamp(np.X, 1, maxX), Math.Clamp(np.Y, 1, maxY));
        if (!FreeAt(e.Pos, r))
        {
            // already overlapping a wall: allow any move whose center is passable
            if (PassPt(np.X, np.Y)) e.Pos = np;
            return;
        }
        if (FreeAt(np, r)) { e.Pos = np; return; }
        var a = new Vec2(np.X, e.Pos.Y);
        if (FreeAt(a, r)) { e.Pos = a; return; }
        var b = new Vec2(e.Pos.X, np.Y);
        if (FreeAt(b, r)) e.Pos = b;
    }

    /// <summary>Advance along current path. Returns true while a path remains.</summary>
    private bool FollowPath(Entity e, float dt)
    {
        var x = e.X;
        var p = x.Path;
        if (p == null || x.PathIdx >= p.Count) return false;
        float step = e.Def.Speed * dt;
        while (x.PathIdx < p.Count)
        {
            var wp = p[x.PathIdx];
            var delta = wp - e.Pos;
            float d = delta.Length;
            bool last = x.PathIdx == p.Count - 1;
            if (d <= (last ? Math.Max(step, 2f) : Math.Max(step, 6f)))
            {
                if (last) { TryMove(e, wp); x.PathIdx++; break; }
                x.PathIdx++;
                continue;
            }
            var dir = delta / d;
            e.Facing = dir;
            TryMove(e, e.Pos + dir * step);
            break;
        }
        return x.PathIdx < p.Count;
    }

    private void MoveStraight(Entity e, Vec2 target, float dt)
    {
        var delta = target - e.Pos;
        float d = delta.Length;
        if (d < 0.5f) return;
        var dir = delta / d;
        e.Facing = dir;
        TryMove(e, e.Pos + dir * Math.Min(d, e.Def.Speed * dt));
    }

    private void SetPath(Entity e, Vec2 goal)
    {
        e.X.Path = FindPath(e.Pos, goal, e.Def.Radius);
        e.X.PathIdx = 0;
        e.X.PathGoal = goal;
        e.X.StuckTimer = 0; e.X.StuckPos = e.Pos;
    }

    private void SetPathToEntity(Entity e, Entity t)
    {
        if (t.Def.IsStructure)
            e.X.Path = FindPath(e.Pos, t.Pos, e.Def.Radius, t.X.TileX, t.X.TileY, t.Def.FootprintTiles);
        else
            e.X.Path = FindPath(e.Pos, t.Pos, e.Def.Radius);
        e.X.PathIdx = 0;
        e.X.PathGoal = t.Pos;
        e.X.RepathTimer = 0.6f + (e.Id % 7) * 0.05f;
    }

    private void ClearPath(Entity e) { e.X.Path = null; e.X.PathIdx = 0; }
    private static bool HasPath(Entity e) => e.X.Path != null && e.X.PathIdx < e.X.Path.Count;

    /// <summary>Approach an entity: repath occasionally, otherwise follow path / go straight.</summary>
    private void Approach(Entity e, Entity t, float dt)
    {
        var x = e.X;
        bool moved = !t.Def.IsStructure && Vec2.DistanceSq(x.PathGoal, t.Pos) > 24f * 24f;
        if ((!HasPath(e) || moved) && x.RepathTimer <= 0) SetPathToEntity(e, t);
        if (!FollowPath(e, dt)) MoveStraight(e, t.Pos, dt);
    }

    // ---------------- separation ----------------
    private readonly List<(Entity e, Vec2 d)> _pushes = new();
    private void Separation(float dt)
    {
        _pushes.Clear();
        foreach (var u in _units)
        {
            if (!u.Alive) continue;
            bool uMoving = HasPath(u);
            bool uHarv = u.Kind == EntityKind.Worker && (u.Order is UnitOrder.Harvest or UnitOrder.ReturnCargo);
            Vec2 acc = Vec2.Zero;
            int cx = Math.Clamp((int)(u.Pos.X / CellSize), 0, _gw - 1), cy = Math.Clamp((int)(u.Pos.Y / CellSize), 0, _gh - 1);
            for (int yy = Math.Max(0, cy - 1); yy <= Math.Min(_gh - 1, cy + 1); yy++)
                for (int xx = Math.Max(0, cx - 1); xx <= Math.Min(_gw - 1, cx + 1); xx++)
                    foreach (var o in _cells[yy * _gw + xx])
                    {
                        if (o == u || !o.Alive) continue;
                        if (uHarv && o.Kind == EntityKind.Worker && (o.Order is UnitOrder.Harvest or UnitOrder.ReturnCargo or UnitOrder.Build)) continue;
                        float rr = u.Def.Radius + o.Def.Radius;
                        var d = u.Pos - o.Pos;
                        float dsq = d.LengthSq;
                        if (dsq >= rr * rr) continue;
                        float dist = MathF.Sqrt(dsq);
                        Vec2 n = dist > 0.01f ? d / dist : new Vec2(((u.Id * 7919) % 100) / 100f - 0.5f, ((u.Id * 104729) % 100) / 100f - 0.5f).Normalized;
                        float overlap = rr - dist;
                        bool oMoving = HasPath(o);
                        float share = uMoving == oMoving ? 0.5f : (uMoving ? 0.25f : 0.75f);
                        if (u.Order == UnitOrder.Hold) share *= 0.2f;
                        acc += n * (overlap * share);
                    }
            if (acc.LengthSq > 0.0001f)
            {
                float max = u.Def.Speed * dt * 1.5f + 0.5f;
                if (acc.Length > max) acc = acc.Normalized * max;
                _pushes.Add((u, acc));
            }
        }
        foreach (var (e, d) in _pushes) TryMove(e, e.Pos + d);
    }

    private void PushOutOfStructures()
    {
        foreach (var u in _units)
        {
            if (!u.Alive) continue;
            var (tx, ty) = WorldToTile(u.Pos);
            if (InBounds(tx, ty) && _occ[ty * Width + tx] != 0)
            {
                var np = NearestPassable(tx, ty, 8);
                if (np != null)
                {
                    var c = TileCenter(np.Value.x, np.Value.y);
                    u.Pos = c;
                    if (HasPath(u)) { u.X.Path = FindPath(u.Pos, u.X.Path![^1], u.Def.Radius); u.X.PathIdx = 0; }
                }
            }
        }
    }
}
