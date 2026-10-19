using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

public static class Minimap
{
    const float TS = World.TileSize;

    /// <summary>Square-fit map area inside the minimap rect.</summary>
    public static RectF MapRect(RectF rc, World w)
    {
        float scale = MathF.Min(rc.W / w.Width, rc.H / w.Height);
        float mw = w.Width * scale, mh = w.Height * scale;
        return new(rc.X + (rc.W - mw) / 2, rc.Y + (rc.H - mh) / 2, mw, mh);
    }

    public static Vec2 ToWorld(RectF rc, Vec2 screen, World w)
    {
        var m = MapRect(rc, w);
        float u = Math.Clamp((screen.X - m.X) / m.W, 0, 1), v = Math.Clamp((screen.Y - m.Y) / m.H, 0, 1);
        return new(u * w.Width * TS, v * w.Height * TS);
    }

    public static Vec2 ToScreen(RectF rc, Vec2 world, World w)
    {
        var m = MapRect(rc, w);
        return new(m.X + world.X / (w.Width * TS) * m.W, m.Y + world.Y / (w.Height * TS) * m.H);
    }

    static Color TileColor(World w, int x, int y)
    {
        byte vis = w.Visibility(GameSession.Me, x, y);
        if (vis == 0) return Color.Black;
        var c = Pal.TerrainColor(w.TerrainAt(x, y));
        return vis == 1 ? Pal.Scale(c, 0.45f) : c;
    }

    public static void Draw(IRenderer2D r, GameSession s, RectF rc)
    {
        var w = s.World;
        r.FillRect(rc.X - 2, rc.Y - 2, rc.W + 4, rc.H + 4, new Color(10, 12, 16));
        var m = MapRect(rc, w);
        float cw = m.W / w.Width, ch = m.H / w.Height;

        // Terrain + fog, merged into horizontal runs of equal color.
        for (int y = 0; y < w.Height; y++)
        {
            int runStart = 0;
            var runCol = TileColor(w, 0, y);
            for (int x = 1; x <= w.Width; x++)
            {
                var c = x < w.Width ? TileColor(w, x, y) : default;
                if (x < w.Width && c == runCol) continue;
                r.FillRect(m.X + runStart * cw, m.Y + y * ch, (x - runStart) * cw + 0.5f, ch + 0.5f, runCol);
                runStart = x; runCol = c;
            }
        }

        // Entities as dots.
        foreach (var e in w.Entities)
        {
            if (!SelectionManager.IsLive(e) || !s.IsVisible(e)) continue;
            var p = ToScreen(rc, e.Pos, w);
            if (e.Kind == EntityKind.CrystalField) { r.FillRect(p.X - 1.5f, p.Y - 1.5f, 3, 3, Pal.Neutral); continue; }
            var col = s.Selection.Contains(e.Id) ? Color.White : Pal.TeamOf(e.Owner);
            if (e.Def.IsStructure)
            {
                float sz = MathF.Max(3, e.Def.FootprintTiles * cw);
                r.FillRect(p.X - sz / 2, p.Y - sz / 2, sz, sz, col);
            }
            else r.FillRect(p.X - 1, p.Y - 1, 2.5f, 2.5f, col);
        }

        // Attack pings.
        foreach (var (pos, t) in s.Pings)
        {
            float age = s.RealTime - t;
            var p = ToScreen(rc, pos, w);
            r.DrawCircle(p, 4 + (age % 1f) * 14, Pal.Fade(Pal.AttackMarker, 1 - (age % 1f)), 2f);
        }

        // Camera rectangle.
        var wv = s.Camera.WorldView;
        var a = ToScreen(rc, new Vec2(wv.X, wv.Y), w);
        var b = ToScreen(rc, new Vec2(wv.Right, wv.Bottom), w);
        float x0 = MathF.Max(m.X, a.X), y0 = MathF.Max(m.Y, a.Y), x1 = MathF.Min(m.Right, b.X), y1 = MathF.Min(m.Bottom, b.Y);
        r.DrawRect(x0, y0, MathF.Max(1, x1 - x0), MathF.Max(1, y1 - y0), Color.White, 1f);
        r.DrawRect(rc.X - 2, rc.Y - 2, rc.W + 4, rc.H + 4, Pal.PanelBorder, 1f);
    }
}
