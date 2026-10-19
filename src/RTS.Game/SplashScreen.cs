using RTS.Core;

namespace RTS.Game;

/// <summary>
/// "Built with BRUTAL" splash shown before gameplay (BRUTAL License v1.0, clause 2.2).
/// NOTE: the licence asks for the official logo from ahwoo.com/brutal-branding; this is a
/// text rendition until the logo image is added as a texture.
/// </summary>
public static class SplashScreen
{
    public const double Duration = 2.0;

    public static void Draw(IRenderer2D r, float t)
    {
        // Fade in 0..0.3s, hold, fade out over the last 0.3s.
        float fade = MathF.Min(1f, MathF.Min(t / 0.3f, ((float)Duration - t) / 0.3f));
        fade = Math.Clamp(fade, 0f, 1f);
        byte a = (byte)(255 * fade);

        r.Clear(new Color(8, 8, 10));
        float cx = r.ScreenWidth / 2f, cy = r.ScreenHeight / 2f;

        const string built = "BUILT WITH";
        const string logo = "BRUTAL";
        var bs = r.MeasureText(built, 22f);
        var ls = r.MeasureText(logo, 96f);

        float boxW = ls.X + 80, boxH = ls.Y + 40;
        float boxX = cx - boxW / 2, boxY = cy - boxH / 2 + 10;
        r.FillRect(boxX, boxY, boxW, boxH, new Color(230, 230, 230, a));
        r.DrawText(cx - ls.X / 2, boxY + (boxH - ls.Y) / 2, logo, new Color(10, 10, 10, a), 96f);
        r.DrawText(cx - bs.X / 2, boxY - bs.Y - 12, built, new Color(200, 200, 200, a), 22f);

        string footer = Credits.BrutalCopyright;
        var fs = r.MeasureText(footer, 16f);
        r.DrawText(cx - fs.X / 2, boxY + boxH + 24, footer, new Color(150, 150, 150, a), 16f);
        const string url = "www.ahwoo.com/brutal";
        var us = r.MeasureText(url, 16f);
        r.DrawText(cx - us.X / 2, boxY + boxH + 46, url, new Color(150, 150, 150, a), 16f);
    }
}
