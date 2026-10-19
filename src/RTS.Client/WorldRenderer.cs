using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

/// <summary>Draws terrain, fog, entities, effects and in-world UI (selection, ghosts, drag box).</summary>
public static class WorldRenderer
{
    const float TS = World.TileSize;

    public static void Draw(IRenderer2D r, GameSession s)
    {
        DrawTerrain(r, s);
        DrawEntities(r, s);
        s.Effects.Draw(r, s.Camera);
        DrawRallies(r, s);
        DrawOrderQueues(r, s);
        DrawPlacementGhost(r, s);
        DrawDragBox(r, s);
    }

    static void DrawTerrain(IRenderer2D r, GameSession s)
    {
        var cam = s.Camera; var w = s.World;
        var wv = cam.WorldView;
        int x0 = Math.Max(0, (int)MathF.Floor(wv.X / TS)), y0 = Math.Max(0, (int)MathF.Floor(wv.Y / TS));
        int x1 = Math.Min(w.Width - 1, (int)MathF.Floor(wv.Right / TS)), y1 = Math.Min(w.Height - 1, (int)MathF.Floor(wv.Bottom / TS));
        float sz = TS * cam.Zoom;
        for (int ty = y0; ty <= y1; ty++)
        {
            for (int tx = x0; tx <= x1; tx++)
            {
                byte vis = w.Visibility(GameSession.Me, tx, ty);
                var p = cam.WorldToScreen(new Vec2(tx * TS, ty * TS));
                if (vis == 0) { r.FillRect(p.X, p.Y, sz + 1, sz + 1, Color.Black); continue; }
                var c = s.TileColors[ty * w.Width + tx];
                if (vis == 1) c = Pal.Scale(c, 0.45f);
                r.FillRect(p.X, p.Y, sz + 1, sz + 1, c);
                if (vis == 2 && sz >= 20)
                {
                    var t = w.TerrainAt(tx, ty);
                    if (t == Terrain.Rock) r.FillRect(p.X + sz * 0.2f, p.Y + sz * 0.25f, sz * 0.25f, sz * 0.2f, Pal.Scale(c, 0.8f));
                    else if (t == Terrain.Water && ((tx + ty * 3) % 5 == 0))
                        r.DrawLine(new Vec2(p.X + sz * 0.2f, p.Y + sz * 0.5f), new Vec2(p.X + sz * 0.6f, p.Y + sz * 0.5f), Pal.Scale(c, 1.3f), 1f);
                }
            }
        }
    }

    static bool OnScreen(Camera cam, Vec2 sp, float margin)
    {
        var v = cam.View;
        return sp.X > v.X - margin && sp.X < v.Right + margin && sp.Y > v.Y - margin && sp.Y < v.Bottom + margin;
    }

    static void DrawEntities(IRenderer2D r, GameSession s)
    {
        var cam = s.Camera; var w = s.World;
        var selected = new HashSet<int>(s.Selection.Ids);
        // Pass order: crystals, structures, units.
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (var e in w.Entities)
            {
                bool crystal = e.Kind == EntityKind.CrystalField;
                int myPass = crystal ? 0 : e.Def.IsStructure ? 1 : 2;
                if (myPass != pass || !SelectionManager.IsLive(e) || !s.IsVisible(e)) continue;
                var sp = cam.WorldToScreen(e.Pos);
                float ext = (e.Def.IsStructure || crystal ? Math.Max(1, e.Def.FootprintTiles) * TS : e.Def.Radius * 2) * cam.Zoom;
                if (!OnScreen(cam, sp, ext + 20)) continue;
                bool sel = selected.Contains(e.Id);
                if (crystal) DrawCrystal(r, s, e, sp, sel);
                else if (e.Def.IsStructure) DrawStructure(r, s, e, sp, sel);
                else DrawUnit(r, s, e, sp, sel);
            }
        }
    }

    static Color SelColor(Entity e) => e.Owner == GameSession.Me ? Pal.Select : e.Owner < 0 ? Pal.Gold : Pal.AttackMarker;

    static bool Flashing(GameSession s, Entity e) => e.LastHitTime > 0 && s.World.Time - e.LastHitTime < 0.15f;

    static void DrawCrystal(IRenderer2D r, GameSession s, Entity e, Vec2 sp, bool sel)
    {
        float z = s.Camera.Zoom;
        float baseR = (e.Def.FootprintTiles > 0 ? e.Def.FootprintTiles * TS / 2 : e.Def.Radius > 0 ? e.Def.Radius : 16) * z;
        float frac = Math.Clamp(e.Resources / 1500f, 0.25f, 1f);
        if (sel) r.DrawCircle(sp, baseR * 1.15f, SelColor(e), 2f);
        // deterministic cluster from id
        for (int i = 0; i < 5; i++)
        {
            float a = (e.Id * 37 + i * 72) * MathF.PI / 180f;
            float d = i == 0 ? 0 : baseR * 0.5f;
            var c = new Vec2(sp.X + MathF.Cos(a) * d, sp.Y + MathF.Sin(a) * d * 0.7f);
            Shapes.Crystal(r, c, baseR * 0.32f * frac * (i == 0 ? 1.3f : 1f), Pal.Neutral);
        }
    }

    static void DrawStructure(IRenderer2D r, GameSession s, Entity e, Vec2 sp, bool sel)
    {
        var cam = s.Camera;
        float size = Math.Max(1, e.Def.FootprintTiles) * TS * cam.Zoom;
        var rc = new RectF(sp.X - size / 2, sp.Y - size / 2, size, size);
        var team = Pal.TeamOf(e.Owner);
        bool flash = Flashing(s, e);

        if (sel) r.DrawRect(rc.X - 3, rc.Y - 3, rc.W + 6, rc.H + 6, SelColor(e), 2f);

        if (e.UnderConstruction)
        {
            r.FillRect(rc.X, rc.Y, rc.W, rc.H, Pal.TeamDarkOf(e.Owner).WithAlpha(150));
            // hazard stripes
            int n = Math.Max(2, (int)(size / 12));
            for (int i = 1; i < n; i++)
            {
                float t = i / (float)n * size;
                r.DrawLine(new Vec2(rc.X + t, rc.Y), new Vec2(rc.X, rc.Y + t), new Color(230, 200, 60, 110), 2f);
                r.DrawLine(new Vec2(rc.Right, rc.Y + t), new Vec2(rc.X + t, rc.Bottom), new Color(230, 200, 60, 110), 2f);
            }
            r.DrawRect(rc.X, rc.Y, rc.W, rc.H, team.WithAlpha(200), 1.5f);
            Ui.Bar(r, rc.X + 3, rc.Bottom - 8, rc.W - 6, 5, e.BuildProgress, Pal.Gold);
        }
        else
        {
            r.FillRect(rc.X, rc.Y, rc.W, rc.H, flash ? new Color(240, 240, 240) : Pal.TeamDarkOf(e.Owner));
            r.DrawRect(rc.X, rc.Y, rc.W, rc.H, team, 2f);
            Shapes.StructureGlyph(r, e.Kind, rc, team, e.Facing);
            if (e.TrainQueue.Count > 0)
            {
                float bt = World.DefOf(e.TrainQueue[0]).BuildTime;
                Ui.Bar(r, rc.X + 3, rc.Bottom - 8, rc.W - 6, 5, bt > 0 ? e.TrainProgress / bt : 0, new Color(120, 200, 255));
                if (size > 30) r.DrawText(rc.Right - 14, rc.Y + 2, e.TrainQueue.Count.ToString(), Pal.Text, 12);
            }
        }
        if (size >= 56) Ui.TextCentered(r, rc.Center.X, rc.Y + 3, e.Def.Name, Pal.Fade(Pal.Text, 0.8f), 11);
        if (sel || e.Hp < World.MaxHpOf(e)) HpBar(r, rc.X, rc.Y - 7, rc.W, e);
    }

    static void DrawUnit(IRenderer2D r, GameSession s, Entity e, Vec2 sp, bool sel)
    {
        float z = s.Camera.Zoom;
        float rad = MathF.Max(3f, (e.Def.Radius > 0 ? e.Def.Radius : 8) * z);
        if (sel) r.DrawCircle(new Vec2(sp.X, sp.Y + rad * 0.25f), rad * 1.45f, SelColor(e), 1.5f);
        var fill = Flashing(s, e) ? Color.White : Pal.TeamOf(e.Owner);
        var outline = Pal.Scale(Pal.TeamOf(e.Owner), 0.45f);
        Shapes.Unit(r, e.Kind, sp, rad, fill, outline, e.Facing);
        if (e.Cargo > 0) r.FillCircle(new Vec2(sp.X + rad * 0.7f, sp.Y - rad * 0.7f), MathF.Max(2, rad * 0.35f), Pal.Neutral);
        if (sel || e.Hp < World.MaxHpOf(e)) HpBar(r, sp.X - rad * 1.3f, sp.Y - rad - 8, rad * 2.6f, e);
        for (int i = 0; i < Math.Min(3, e.Rank); i++)
            Shapes.Chevron(r, new Vec2(sp.X + rad * 1.5f + 3, sp.Y - rad * 0.2f - i * 4), 3f, Pal.Gold);
    }

    static void HpBar(IRenderer2D r, float x, float y, float w, Entity e)
    {
        float f = World.MaxHpOf(e) > 0 ? e.Hp / (float)World.MaxHpOf(e) : 1f;
        r.FillRect(x - 1, y - 1, w + 2, 6, new Color(0, 0, 0, 200));
        r.FillRect(x, y, w * Math.Clamp(f, 0, 1), 4, Pal.HpColor(f));
    }

    static void DrawRallies(IRenderer2D r, GameSession s)
    {
        foreach (var e in s.Selection.Entities(s.World))
        {
            if (e.Owner != GameSession.Me || !e.Def.IsStructure || e.RallyPoint is not Vec2 rp) continue;
            var a = s.Camera.WorldToScreen(e.Pos); var b = s.Camera.WorldToScreen(rp);
            r.DrawLine(a, b, new Color(240, 210, 80, 170), 1.5f);
            r.DrawLine(b, new Vec2(b.X, b.Y - 16), Pal.Gold, 2f);
            r.FillTriangle(new Vec2(b.X, b.Y - 16), new Vec2(b.X + 10, b.Y - 12), new Vec2(b.X, b.Y - 8), Pal.Gold);
        }
    }

    /// <summary>How one segment of a unit's order chain is drawn, by the order the segment leads to.
    /// Per-order-type looks (dashed attack-move, dotted build, ...) belong in LineStyleFor.</summary>
    readonly record struct OrderLineStyle(Color Color, float Width);

    static readonly Color GhostOk = new(60, 220, 90);

    static OrderLineStyle LineStyleFor(UnitOrder o) => o switch
    {
        UnitOrder.AttackMove or UnitOrder.Attack => new(Pal.AttackMarker, 1.5f),
        UnitOrder.Harvest => new(Pal.Neutral, 1.5f),
        _ => new(Pal.MoveMarker, 1.5f), // move, build (same as their click markers)
    };

    /// <summary>World point an order heads for, or null if it has none (or its target is gone).</summary>
    static Vec2? OrderPoint(World w, QueuedOrder o)
    {
        if (o.Order is UnitOrder.Idle or UnitOrder.Hold) return null;
        if (o.Target is Vec2 p) return p; // e.g. an attack-move heads for its destination even while fighting
        return o.TargetId is int id && w.Get(id) is { Alive: true } t ? t.Pos : null;
    }

    /// <summary>Selected own units: line chain through the current and queued order targets, and
    /// ghost footprints for builds not yet placed.</summary>
    static void DrawOrderQueues(IRenderer2D r, GameSession s)
    {
        var w = s.World; var cam = s.Camera;
        foreach (var e in s.Selection.Entities(w))
        {
            if (e.Owner != GameSession.Me || e.Def.IsStructure) continue;
            var cur = w.CurrentOrderOf(e);
            var queue = w.QueueOf(e);
            if (World.IsPlacement(cur)) DrawFootprintGhost(r, s, cur.BuildKind, cur.BuildTx, cur.BuildTy, GhostOk.WithAlpha(150), 25);
            foreach (var o in queue)
                if (World.IsPlacement(o)) DrawFootprintGhost(r, s, o.BuildKind, o.BuildTx, o.BuildTy, GhostOk.WithAlpha(150), 25);
            if (queue.Count == 0) continue;

            var from = cam.WorldToScreen(e.Pos);
            void Segment(QueuedOrder o)
            {
                if (OrderPoint(w, o) is not Vec2 p) return;
                var to = cam.WorldToScreen(p);
                var st = LineStyleFor(o.Order);
                r.DrawLine(from, to, st.Color.WithAlpha(170), st.Width);
                r.FillCircle(to, 3f, st.Color);
                from = to;
            }
            if (cur.Order != UnitOrder.ReturnCargo) Segment(cur); // a return trip's TargetId is the field, not where it heads
            foreach (var o in queue) Segment(o);
        }
    }

    static void DrawFootprintGhost(IRenderer2D r, GameSession s, EntityKind k, int tx, int ty, Color col, byte fillAlpha)
    {
        int n = Math.Max(1, World.DefOf(k).FootprintTiles);
        var p = s.Camera.WorldToScreen(new Vec2(tx * TS, ty * TS));
        float sz = n * TS * s.Camera.Zoom, ts = TS * s.Camera.Zoom;
        r.FillRect(p.X, p.Y, sz, sz, col.WithAlpha(fillAlpha));
        for (int i = 1; i < n; i++)
        {
            r.DrawLine(new Vec2(p.X + i * ts, p.Y), new Vec2(p.X + i * ts, p.Y + sz), col.WithAlpha(90), 1f);
            r.DrawLine(new Vec2(p.X, p.Y + i * ts), new Vec2(p.X + sz, p.Y + i * ts), col.WithAlpha(90), 1f);
        }
        r.DrawRect(p.X, p.Y, sz, sz, col, 2f);
        Shapes.StructureGlyph(r, k, new RectF(p.X, p.Y, sz, sz), col.WithAlpha(180), new Vec2(1, 0));
    }

    static void DrawPlacementGhost(IRenderer2D r, GameSession s)
    {
        if (s.Placing is not EntityKind k) return;
        if (!s.Layout.PlayArea.Contains(s.MouseScreen)) return;
        var (tx, ty) = s.PlacementTile(k);
        bool ok = s.World.CanPlace(GameSession.Me, k, tx, ty);
        DrawFootprintGhost(r, s, k, tx, ty, ok ? GhostOk : new Color(230, 60, 50), 70);
    }

    static void DrawDragBox(IRenderer2D r, GameSession s)
    {
        if (!s.Dragging || Vec2.Distance(s.DragStart, s.MouseScreen) < GameSession.DragThreshold) return;
        var rc = RectF.FromCorners(s.DragStart, s.MouseScreen);
        r.FillRect(rc.X, rc.Y, rc.W, rc.H, new Color(90, 240, 120, 35));
        r.DrawRect(rc.X, rc.Y, rc.W, rc.H, Pal.Select, 1f);
    }
}
