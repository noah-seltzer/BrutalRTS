using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

public static class Hud
{
    public static readonly string[] HelpLines =
    {
        "Left-click / drag: select (Shift add/toggle, double-click or Ctrl-click: all of type on screen)",
        "Right-click: smart command (move / attack / harvest / help build / set rally)",
        "A + left-click: attack-move     S: stop     H: hold position (stop/hold clear queued orders)",
        "Shift + order (right-click, attack-move, build placement): queue it after the current orders",
        "Worker build: B Barracks, D Supply Depot, F Factory, T Turret, Q HQ",
        "Train: W Worker, E Soldier, R Ranger, T Tank     X: cancel last queued",
        "Ctrl+0-9: assign group   0-9: recall (double-tap: center camera)",
        "Camera: arrow keys, screen edges, middle-drag, wheel zoom, minimap click",
        "Space: jump to last alert   P: pause   F2/F3: game speed   F1: help   F10: quit to menu",
    };

    const float CellSize = 36f, CellGap = 3f;

    public static bool InfoCell(Layout L, int i, out RectF rc)
    {
        var a = L.InfoArea;
        int cols = Math.Max(1, (int)((a.W - 8) / (CellSize + CellGap)));
        int rows = Math.Max(1, (int)((a.H - 26) / (CellSize + CellGap)));
        int cx = i % cols, cy = i / cols;
        rc = new RectF(a.X + 4 + cx * (CellSize + CellGap), a.Y + 22 + cy * (CellSize + CellGap), CellSize, CellSize);
        return cy < rows;
    }

    public static void Draw(IRenderer2D r, GameSession s)
    {
        var L = s.Layout;
        DrawMessages(r, s);
        DrawCursorHint(r, s);
        DrawTopBar(r, s);

        Ui.Panel(r, L.Panel, new Color(18, 21, 27, 250));
        r.DrawLine(new Vec2(0, L.Panel.Y), new Vec2(L.W, L.Panel.Y), new Color(95, 110, 130), 2f);
        Minimap.Draw(r, s, L.Minimap);
        DrawInfo(r, s);
        DrawCard(r, s);
        DrawTooltip(r, s);

        if (s.Paused && !s.GameOver) DrawCenterBanner(r, L, "PAUSED", "Press P to resume");
        if (s.ShowHelp) DrawHelp(r, L);
        if (s.GameOver) DrawGameOver(r, s);
    }

    static void DrawTopBar(IRenderer2D r, GameSession s)
    {
        var L = s.Layout;
        var p = s.MyPlayer;
        r.FillRect(0, 0, L.W, Layout.TopBarH, new Color(16, 19, 24, 245));
        r.DrawLine(new Vec2(0, Layout.TopBarH), new Vec2(L.W, Layout.TopBarH), Pal.PanelBorder, 1f);
        float x = 10, y = 6;
        Shapes.Crystal(r, new Vec2(x + 6, y + 11), 6, Pal.Neutral);
        x += 18;
        x = Seg(r, x, y, $"{p.Credits}", Pal.Gold);
        var popCol = p.Pop >= p.PopCap ? Pal.AttackMarker : Pal.Text;
        x = Seg(r, x, y, $"Supply {p.Pop}/{p.PopCap}", popCol);
        int t = (int)s.World.Time;
        x = Seg(r, x, y, $"{t / 60:00}:{t % 60:00}", Pal.Text);
        x = Seg(r, x, y, s.Difficulty.ToString(), Pal.TextDim);
        if (s.Speed != 1f) x = Seg(r, x, y, $"Speed x{s.Speed:0.#}", Pal.Gold);
        if (s.Paused) r.DrawText(x, y, "PAUSED", Pal.AttackMarker, 16);
        var help = "F1 Help";
        r.DrawText(L.W - r.MeasureText(help, 14).X - 10, 7, help, Pal.TextDim, 14);
    }

    static float Seg(IRenderer2D r, float x, float y, string text, Color c)
    {
        r.DrawText(x, y, text, c, 16);
        return x + r.MeasureText(text, 16).X + 24;
    }

    static void DrawMessages(IRenderer2D r, GameSession s)
    {
        var L = s.Layout;
        float y = L.PlayArea.Bottom - 26;
        for (int i = s.Messages.Items.Count - 1; i >= 0; i--)
        {
            var m = s.Messages.Items[i];
            float age = s.RealTime - m.Time;
            float a = age < MessageLog.Life - 1.5f ? 1f : Math.Clamp((MessageLog.Life - age) / 1.5f, 0, 1);
            Ui.TextShadow(r, 12, y, m.Text, Pal.Fade(m.Color, a), 16);
            y -= 20;
        }
    }

    static void DrawCursorHint(IRenderer2D r, GameSession s)
    {
        string? hint = s.AttackMovePending ? "Attack-move: left-click target (right-click cancels)"
            : s.Placing is EntityKind k ? $"Place {World.DefOf(k).Name} (Shift: queue more on one worker, right-click/Esc cancels)" : null;
        if (hint == null) return;
        var m = s.MouseScreen;
        Ui.TextShadow(r, m.X + 16, m.Y + 14, hint, s.AttackMovePending ? Pal.AttackMarker : Pal.Gold, 13);
        if (s.AttackMovePending)
        {
            r.DrawCircle(m, 9, Pal.AttackMarker, 1.5f);
            r.DrawLine(new Vec2(m.X - 13, m.Y), new Vec2(m.X + 13, m.Y), Pal.AttackMarker, 1f);
            r.DrawLine(new Vec2(m.X, m.Y - 13), new Vec2(m.X, m.Y + 13), Pal.AttackMarker, 1f);
        }
    }

    static void DrawInfo(IRenderer2D r, GameSession s)
    {
        var L = s.Layout;
        var a = L.InfoArea;
        Ui.Panel(r, a, new Color(26, 30, 38));
        var ents = s.Selection.Entities(s.World).ToList();
        if (ents.Count == 0)
        {
            r.DrawText(a.X + 10, a.Y + 10, "Nothing selected", Pal.TextDim, 15);
            r.DrawText(a.X + 10, a.Y + 32, "Drag a box or click a unit. F1 for controls.", Pal.TextDim, 13);
            return;
        }
        if (ents.Count == 1) { DrawSingle(r, s, ents[0], a); return; }

        var header = $"{ents.Count} selected";
        r.DrawText(a.X + 6, a.Y + 3, header, Pal.Text, 15);
        int queued = ents.Count(e => e.Owner == GameSession.Me && s.World.QueueOf(e).Count > 0);
        if (queued > 0) r.DrawText(a.X + 6 + r.MeasureText(header, 15).X + 12, a.Y + 5, $"{queued} with queued orders", Pal.TextDim, 13);
        for (int i = 0; i < ents.Count; i++)
        {
            if (!InfoCell(L, i, out var rc))
            {
                r.DrawText(a.Right - 60, a.Y + 3, $"+{ents.Count - i} more", Pal.TextDim, 13);
                break;
            }
            var e = ents[i];
            bool hov = rc.Contains(s.MouseScreen);
            r.FillRect(rc.X, rc.Y, rc.W, rc.H, hov ? new Color(55, 65, 82) : new Color(36, 42, 52));
            r.DrawRect(rc.X, rc.Y, rc.W, rc.H, Pal.PanelBorder, 1f);
            Shapes.Icon(r, e.Kind, new RectF(rc.X + 2, rc.Y + 1, rc.W - 4, rc.H - 8), e.Owner);
            float f = World.MaxHpOf(e) > 0 ? e.Hp / (float)World.MaxHpOf(e) : 1f;
            Ui.Bar(r, rc.X + 3, rc.Bottom - 6, rc.W - 6, 3, f, Pal.HpColor(f));
            if (e.Rank > 0) r.DrawText(rc.X + 2, rc.Y + 1, new string('^', e.Rank), Pal.Gold, 10);
        }
    }

    static void DrawSingle(IRenderer2D r, GameSession s, Entity e, RectF a)
    {
        var icon = new RectF(a.X + 8, a.Y + 8, 64, 64);
        r.FillRect(icon.X, icon.Y, icon.W, icon.H, new Color(36, 42, 52));
        Shapes.Icon(r, e.Kind, icon, e.Owner);
        float x = icon.Right + 12, y = a.Y + 6;
        float maxW = MathF.Max(20, a.Right - x - 6);
        string owner = e.Owner == GameSession.Me ? "" : e.Owner < 0 ? " (neutral)" : " (enemy)";
        Ui.TextFit(r, x, y, e.Def.Name + owner, Pal.TeamOf(e.Owner), 18, maxW); y += 24;

        if (e.Kind == EntityKind.CrystalField)
        {
            Ui.TextFit(r, x, y, $"Crystals remaining: {e.Resources}", Pal.Neutral, 15, maxW);
            return;
        }
        float f = World.MaxHpOf(e) > 0 ? e.Hp / (float)World.MaxHpOf(e) : 1f;
        Ui.Bar(r, x, y + 2, 160, 12, f, Pal.HpColor(f));
        r.DrawText(x + 168, y, $"{e.Hp}/{World.MaxHpOf(e)}", Pal.Text, 14); y += 22;

        var parts = new List<string>();
        if (!e.Def.IsStructure)
        {
            parts.Add($"Rank {e.Rank}");
            parts.Add($"Kills {e.Kills}");
            if (e.Def.Builds.Length > 0) parts.Add($"Cargo {e.Cargo}");
            parts.Add(e.Order.ToString());
        }
        if (e.Def.Damage > 0) parts.Add($"Dmg {e.Def.Damage}");
        Ui.TextFit(r, x, y, string.Join("  ", parts), Pal.TextDim, 14, maxW);
        for (int i = 0; i < Math.Min(3, e.Rank); i++) Shapes.Chevron(r, new Vec2(icon.X + 8 + i * 10, icon.Bottom + 10), 4, Pal.Gold);
        y += 22;

        if (!e.Def.IsStructure && e.Owner == GameSession.Me && s.World.QueueOf(e).Count > 0)
        {
            var orders = new[] { s.World.CurrentOrderOf(e) }.Concat(s.World.QueueOf(e)).Select(OrderLabel);
            DrawWrapped(r, x, y, "Orders: " + string.Join(" > ", orders), Pal.Text, 14, maxW, a.Bottom - 4);
            return;
        }

        if (e.UnderConstruction)
        {
            Ui.TextFit(r, x, y, $"Under construction {e.BuildProgress * 100:0}%", Pal.Gold, 14, maxW);
            Ui.Bar(r, x, y + 20, 200, 8, e.BuildProgress, Pal.Gold);
            return;
        }
        if (e.Def.IsStructure && e.Def.PopProvided > 0) { Ui.TextFit(r, x, y, $"Provides {e.Def.PopProvided} supply", Pal.TextDim, 14, maxW); y += 20; }

        if (e.TrainQueue.Count > 0 && e.Owner == GameSession.Me)
        {
            r.DrawText(x, y, "Queue:", Pal.TextDim, 14);
            float qx = x + 52;
            for (int i = 0; i < e.TrainQueue.Count && i < 8; i++)
            {
                var rc = new RectF(qx + i * 34, y - 2, 30, 30);
                r.FillRect(rc.X, rc.Y, rc.W, rc.H, new Color(36, 42, 52));
                Shapes.Icon(r, e.TrainQueue[i], rc, e.Owner);
                if (i == 0)
                {
                    float bt = World.DefOf(e.TrainQueue[0]).BuildTime;
                    Ui.Bar(r, rc.X, rc.Bottom + 2, rc.W, 4, bt > 0 ? e.TrainProgress / bt : 0, new Color(120, 200, 255));
                }
            }
        }
        else if (e.Def.Trains.Length > 0 && e.Owner == GameSession.Me)
        {
            Ui.TextFit(r, x, y, "Right-click: set rally point", Pal.TextDim, 13, maxW);
        }
    }

    static string OrderLabel(QueuedOrder o) => o.Order switch
    {
        UnitOrder.Build => $"Build {World.DefOf(o.BuildKind).Name}",
        UnitOrder.AttackMove => "Attack-move",
        UnitOrder.ReturnCargo => "Return cargo",
        _ => o.Order.ToString(),
    };

    /// <summary>Word-wraps text into lines of maxW, stopping before bottom.</summary>
    static void DrawWrapped(IRenderer2D r, float x, float y, string text, Color c, float size, float maxW, float bottom)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && r.MeasureText(next, size).X > maxW)
            {
                if (y + size * 2 + 4 > bottom) { Ui.TextFit(r, x, y, line + " ...", c, size, maxW); return; }
                r.DrawText(x, y, line, c, size);
                y += size + 4;
                line = word;
            }
            else line = next;
        }
        if (line.Length > 0) Ui.TextFit(r, x, y, line, c, size, maxW);
    }

    static void DrawCard(IRenderer2D r, GameSession s)
    {
        var L = s.Layout;
        for (int i = 0; i < Layout.CardCols * Layout.CardRows; i++)
        {
            var rc = L.CardSlot(i);
            r.FillRect(rc.X, rc.Y, rc.W, rc.H, new Color(26, 30, 38));
            r.DrawRect(rc.X, rc.Y, rc.W, rc.H, new Color(45, 52, 62), 1f);
        }
        // One shared size for text-only labels so the card looks uniform (largest that fits every label).
        float textSize = 14f;
        foreach (var b in s.Card)
            if (b.Icon == null) textSize = MathF.Min(textSize, Ui.Fit(r, b.Label, Layout.BtnW - 8, 14f).Size);
        foreach (var b in s.Card)
        {
            var rc = L.CardSlot(b.Slot);
            bool hov = b == s.HoveredButton;
            bool active = (b.Hotkey == Key.A && s.AttackMovePending) || (b.Icon != null && s.Placing == b.Icon);
            Ui.Button(r, rc, "", b.Enabled, hov, active);
            var tc = b.Enabled ? Pal.Text : Pal.TextDim;
            if (b.Icon is EntityKind k)
            {
                var ic = new RectF(rc.X + rc.W / 2 - 14, rc.Y + 4, 28, 28);
                if (b.Enabled) Shapes.Icon(r, k, ic, GameSession.Me);
                else Shapes.Icon(r, k, ic, -2);
                Ui.TextFitCentered(r, rc.Center.X, rc.Y + 34, b.Label, tc, 11, rc.W - 6);
            }
            else Ui.TextFitCentered(r, rc.Center.X, rc.Y + (rc.H - textSize) / 2 + 2, b.Label, tc, textSize, rc.W - 8);
            if (b.Cost > 0)
            {
                bool afford = s.MyPlayer.Credits >= b.Cost;
                r.DrawText(rc.X + 3, rc.Y + 2, b.Cost.ToString(), afford ? Pal.Gold : Pal.AttackMarker, 10);
            }
            var key = b.ShownKey;
            if (key.Length > 0)
            {
                var ks = r.MeasureText(key, 11);
                r.DrawText(rc.Right - ks.X - 3, rc.Y + 2, key, Pal.Text, 11);
            }
        }
    }

    static void DrawTooltip(IRenderer2D r, GameSession s)
    {
        var b = s.HoveredButton;
        if (b == null) return;
        var lines = new List<(string, Color, float)> { (b.Title + (b.ShownKey.Length > 0 ? $"  [{b.ShownKey}]" : ""), Pal.Text, 15) };
        foreach (var d in b.Detail.Split('\n', StringSplitOptions.RemoveEmptyEntries)) lines.Add((d, Pal.TextDim, 13));
        if (!b.Enabled && b.DisabledReason != null) lines.Add((b.DisabledReason, Pal.AttackMarker, 13));
        float w = lines.Max(l => r.MeasureText(l.Item1, l.Item3).X) + 16;
        float h = lines.Sum(l => l.Item3 + 5) + 10;
        var L = s.Layout;
        float x = MathF.Min(L.CardArea.X, L.W - w - 6), y = L.Panel.Y - h - 6;
        Ui.Panel(r, new RectF(x, y, w, h), new Color(14, 16, 20, 240), new Color(120, 135, 160));
        float ly = y + 6;
        foreach (var (t, c, sz) in lines) { r.DrawText(x + 8, ly, t, c, sz); ly += sz + 5; }
    }

    static void DrawCenterBanner(IRenderer2D r, Layout L, string title, string sub)
    {
        var p = L.PlayArea;
        r.FillRect(p.X, p.Center.Y - 50, p.W, 100, new Color(0, 0, 0, 150));
        Ui.TextCentered(r, p.Center.X, p.Center.Y - 34, title, Pal.Gold, 40);
        Ui.TextCentered(r, p.Center.X, p.Center.Y + 14, sub, Pal.Text, 16);
    }

    static void DrawHelp(IRenderer2D r, Layout L)
    {
        float w = MathF.Min(L.W - 20, HelpLines.Max(l => r.MeasureText(l, 14).X) + 40), h = 60 + HelpLines.Length * 24;
        var rc = new RectF((L.W - w) / 2, L.PlayArea.Y + 20, w, h);
        Ui.Panel(r, rc, new Color(12, 14, 18, 235), Pal.Gold);
        Ui.TextCentered(r, rc.Center.X, rc.Y + 10, "CONTROLS", Pal.Gold, 22);
        float y = rc.Y + 44;
        foreach (var l in HelpLines) { Ui.TextFit(r, rc.X + 20, y, l, Pal.Text, 14, w - 40); y += 24; }
    }

    static void DrawGameOver(IRenderer2D r, GameSession s)
    {
        var L = s.Layout;
        bool won = s.Winner == GameSession.Me;
        r.FillRect(0, 0, L.W, L.H, new Color(0, 0, 0, 160));
        float w = 460, h = 280;
        var rc = new RectF((L.W - w) / 2, (L.H - h) / 2, w, h);
        Ui.Panel(r, rc, new Color(16, 18, 24, 245), won ? Pal.Gold : Pal.AttackMarker);
        Ui.TextCentered(r, rc.Center.X, rc.Y + 18, won ? "VICTORY" : "DEFEAT", won ? Pal.Gold : Pal.AttackMarker, 48);
        int t = (int)s.World.Time;
        var st = s.Stats;
        string[] lines =
        {
            $"Game time: {t / 60:00}:{t % 60:00}   ({s.Difficulty})",
            $"Units trained: {st.UnitsTrained}",
            $"Units lost: {st.UnitsLost}",
            $"Enemy units destroyed: {st.EnemyUnitsKilled}",
            $"Structures built: {st.StructuresBuilt}",
        };
        float y = rc.Y + 84;
        foreach (var l in lines) { Ui.TextCentered(r, rc.Center.X, y, l, Pal.Text, 16); y += 24; }
        Ui.TextCentered(r, rc.Center.X, rc.Bottom - 34, "Enter: back to menu", Pal.Gold, 18);
    }
}
