using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

public enum MenuAction { None, Start, Quit }

public sealed class MainMenu
{
    public Difficulty Selected = Difficulty.Normal;
    Vec2 _mouse;
    float _time;

    static readonly Difficulty[] Diffs = { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard };

    public static RectF DiffButton(Layout L, int i)
    {
        float bw = 140, gap = 16, total = 3 * bw + 2 * gap;
        return new((L.W - total) / 2 + i * (bw + gap), L.H * 0.36f, bw, 44);
    }
    public static RectF StartButton(Layout L) => new((L.W - 240) / 2f, L.H * 0.36f + 70, 240, 52);
    public static RectF QuitButton(Layout L) => new((L.W - 240) / 2f, L.H * 0.36f + 132, 240, 40);

    public MenuAction Update(InputState input, float dt, Layout L)
    {
        _time += dt;
        _mouse = input.MousePos;
        if (input.Pressed(Key.Num1)) Selected = Difficulty.Easy;
        if (input.Pressed(Key.Num2)) Selected = Difficulty.Normal;
        if (input.Pressed(Key.Num3)) Selected = Difficulty.Hard;
        if (input.Pressed(Key.Escape)) return MenuAction.Quit;
        if (input.Pressed(Key.Enter) || input.Pressed(Key.Space)) return MenuAction.Start;
        if (input.MousePressed[0])
        {
            for (int i = 0; i < 3; i++)
                if (DiffButton(L, i).Contains(_mouse)) Selected = Diffs[i];
            if (StartButton(L).Contains(_mouse)) return MenuAction.Start;
            if (QuitButton(L).Contains(_mouse)) return MenuAction.Quit;
        }
        return MenuAction.None;
    }

    public void Draw(IRenderer2D r, Layout L)
    {
        r.Clear(new Color(12, 14, 20));
        // backdrop: drifting grid
        float off = (_time * 12) % 48;
        var gc = new Color(28, 34, 46);
        for (float x = -off; x < L.W; x += 48) r.DrawLine(new Vec2(x, 0), new Vec2(x, L.H), gc, 1f);
        for (float gy = -off; gy < L.H; gy += 48) r.DrawLine(new Vec2(0, gy), new Vec2(L.W, gy), gc, 1f);

        float ty = L.H * 0.12f;
        Ui.TextCentered(r, L.W / 2f + 3, ty + 3, "BRUTAL RTS", new Color(0, 0, 0), 64);
        Ui.TextCentered(r, L.W / 2f, ty, "BRUTAL RTS", Pal.Gold, 64);
        Ui.TextCentered(r, L.W / 2f, ty + 74, "Gather crystals. Build an army. Crush the enemy base.", Pal.TextDim, 16);

        Ui.TextCentered(r, L.W / 2f, DiffButton(L, 0).Y - 26, "Difficulty (1 / 2 / 3)", Pal.Text, 16);
        for (int i = 0; i < 3; i++)
        {
            var rc = DiffButton(L, i);
            Ui.Button(r, rc, $"{i + 1}  {Diffs[i]}", true, rc.Contains(_mouse), Selected == Diffs[i], 18);
        }
        var sb = StartButton(L);
        Ui.Button(r, sb, "START  (Enter)", true, sb.Contains(_mouse), false, 22);
        var qb = QuitButton(L);
        Ui.Button(r, qb, "Quit  (Esc)", true, qb.Contains(_mouse), false, 16);

        float y = qb.Bottom + 30;
        Ui.TextCentered(r, L.W / 2f, y, "CONTROLS", Pal.Gold, 16);
        y += 24;
        foreach (var l in Hud.HelpLines)
        {
            Ui.TextFitCentered(r, L.W / 2f, y, l, Pal.TextDim, 13, L.W - 20);
            y += 19;
        }
    }
}
