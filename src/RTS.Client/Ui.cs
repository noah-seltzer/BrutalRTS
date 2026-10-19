using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

public readonly record struct RectF(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public Vec2 Center => new(X + W / 2, Y + H / 2);
    public bool Contains(Vec2 p) => p.X >= X && p.X < X + W && p.Y >= Y && p.Y < Y + H;
    public static RectF FromCorners(Vec2 a, Vec2 b)
        => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));
}

/// <summary>Screen layout; recomputed from the renderer size each Draw.</summary>
public sealed class Layout
{
    public const float TopBarH = 28f, PanelH = 190f, EdgeScroll = 12f;
    public const int CardCols = 4, CardRows = 3;
    public const float BtnW = 60f, BtnH = 52f, BtnGap = 4f;

    public int W = 1280, H = 720;

    public RectF TopBar => new(0, 0, W, TopBarH);
    public RectF Panel => new(0, H - PanelH, W, PanelH);
    /// <summary>Area where the world is visible (between top bar and HUD panel).</summary>
    public RectF PlayArea => new(0, TopBarH, W, MathF.Max(1, H - TopBarH - PanelH));
    public RectF Minimap => new(8, H - PanelH + 8, PanelH - 16, PanelH - 16);
    public RectF CardArea
    {
        get
        {
            float w = CardCols * BtnW + (CardCols - 1) * BtnGap;
            float h = CardRows * BtnH + (CardRows - 1) * BtnGap;
            return new(W - w - 8, H - PanelH + 8 + (PanelH - 16 - h) / 2, w, h);
        }
    }
    public RectF CardSlot(int slot)
    {
        var a = CardArea;
        int cx = slot % CardCols, cy = slot / CardCols;
        return new(a.X + cx * (BtnW + BtnGap), a.Y + cy * (BtnH + BtnGap), BtnW, BtnH);
    }
    public RectF InfoArea
    {
        get
        {
            var m = Minimap; var c = CardArea;
            return new(m.Right + 12, H - PanelH + 8, MathF.Max(10, c.X - m.Right - 24), PanelH - 16);
        }
    }
}

public static class Pal
{
    public static readonly Color[] Team = { new(70, 140, 240), new(225, 65, 60) };
    public static readonly Color[] TeamDark = { new(30, 60, 120), new(110, 30, 28) };
    public static readonly Color Neutral = new(120, 225, 245);
    public static readonly Color PanelBg = new(22, 26, 32, 240);
    public static readonly Color PanelBorder = new(70, 80, 95);
    public static readonly Color Text = new(225, 230, 235);
    public static readonly Color TextDim = new(140, 148, 158);
    public static readonly Color Gold = new(245, 205, 80);
    public static readonly Color MoveMarker = new(80, 230, 110);
    public static readonly Color AttackMarker = new(240, 70, 60);
    public static readonly Color Select = new(90, 240, 120);
    public static readonly Color Grass = new(58, 102, 50);
    public static readonly Color Water = new(36, 70, 128);
    public static readonly Color Rock = new(104, 98, 90);

    /// <summary>owner -2 = "disabled" grey (HUD icons).</summary>
    public static Color TeamOf(int owner) => owner is 0 or 1 ? Team[owner] : owner == -2 ? new Color(110, 112, 118) : Neutral;
    public static Color TeamDarkOf(int owner) => owner is 0 or 1 ? TeamDark[owner] : owner == -2 ? new Color(45, 46, 50) : new Color(40, 90, 100);
    public static Color TerrainColor(Terrain t) => t switch { Terrain.Water => Water, Terrain.Rock => Rock, _ => Grass };

    public static Color Scale(Color c, float k)
        => new((byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255), c.A);
    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
    }
    public static Color Fade(Color c, float alpha01) => c.WithAlpha((byte)Math.Clamp(c.A * alpha01, 0, 255));
    public static Color HpColor(float frac) => frac > 0.5f ? new Color(70, 210, 90) : frac > 0.25f ? new Color(235, 200, 60) : new Color(225, 60, 50);
}

public static class Ui
{
    public static void Panel(IRenderer2D r, RectF rc, Color? fill = null, Color? border = null)
    {
        r.FillRect(rc.X, rc.Y, rc.W, rc.H, fill ?? Pal.PanelBg);
        r.DrawRect(rc.X, rc.Y, rc.W, rc.H, border ?? Pal.PanelBorder, 1f);
    }

    public static void TextCentered(IRenderer2D r, float cx, float y, string text, Color c, float size)
    {
        var sz = r.MeasureText(text, size);
        r.DrawText(cx - sz.X / 2, y, text, c, size);
    }

    public static void TextShadow(IRenderer2D r, float x, float y, string text, Color c, float size)
    {
        r.DrawText(x + 1, y + 1, text, new Color(0, 0, 0, c.A), size);
        r.DrawText(x, y, text, c, size);
    }

    public static void Button(IRenderer2D r, RectF rc, string label, bool enabled, bool hovered, bool selected = false, float size = 16f)
    {
        var fill = !enabled ? new Color(45, 47, 52) : selected ? new Color(50, 95, 150) : hovered ? new Color(60, 72, 90) : new Color(38, 46, 58);
        r.FillRect(rc.X, rc.Y, rc.W, rc.H, fill);
        r.DrawRect(rc.X, rc.Y, rc.W, rc.H, selected ? Pal.Gold : hovered && enabled ? new Color(150, 170, 200) : Pal.PanelBorder, selected ? 2f : 1f);
        if (label.Length == 0) return;
        var (text, sz) = Fit(r, label, rc.W - 8, size);
        var m = r.MeasureText(text, sz);
        r.DrawText(rc.X + (rc.W - m.X) / 2, rc.Y + (rc.H - m.Y) / 2, text, enabled ? Pal.Text : Pal.TextDim, sz);
    }

    /// <summary>
    /// Shrinks the font size (down to minSize) and then truncates until the text measures within maxW.
    /// Relies on the renderer's own MeasureText so it fits under every backend's font.
    /// </summary>
    public static (string Text, float Size) Fit(IRenderer2D r, string text, float maxW, float size, float minSize = 8f)
    {
        float s = size;
        while (s > minSize && r.MeasureText(text, s).X > maxW) s -= 1f;
        if (r.MeasureText(text, s).X <= maxW) return (text, s);
        for (int n = text.Length - 1; n > 0; n--)
        {
            var t = text[..n].TrimEnd() + ".";
            if (r.MeasureText(t, s).X <= maxW) return (t, s);
        }
        return (text[..1], s);
    }

    public static void TextFitCentered(IRenderer2D r, float cx, float y, string text, Color c, float size, float maxW)
    {
        var (t, s) = Fit(r, text, maxW, size);
        TextCentered(r, cx, y, t, c, s);
    }

    public static void TextFit(IRenderer2D r, float x, float y, string text, Color c, float size, float maxW)
    {
        var (t, s) = Fit(r, text, maxW, size);
        r.DrawText(x, y, t, c, s);
    }

    public static void Bar(IRenderer2D r, float x, float y, float w, float h, float frac, Color fg, Color? bg = null)
    {
        frac = Math.Clamp(frac, 0, 1);
        r.FillRect(x, y, w, h, bg ?? new Color(0, 0, 0, 180));
        if (frac > 0) r.FillRect(x, y, w * frac, h, fg);
    }

    public static string KeyName(Key k) => k switch
    {
        >= Key.Num0 and <= Key.Num9 => ((int)(k - Key.Num0)).ToString(),
        Key.Escape => "Esc",
        Key.Unknown => "",
        _ => k.ToString(),
    };
}
