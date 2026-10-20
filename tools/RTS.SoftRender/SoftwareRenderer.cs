using RTS.Core;

namespace RTS.SoftRender;

/// <summary>
/// CPU implementation of <see cref="IRenderer2D"/> into a packed RGBA framebuffer
/// (uint = R | G&lt;&lt;8 | B&lt;&lt;16 | A&lt;&lt;24, same layout as <see cref="Color.ToAbgr"/>).
/// Source-over alpha blending, everything clipped to the framebuffer. Shapes are rasterized as
/// non-overlapping horizontal spans (pixel-center sampling) so translucent shapes blend exactly once.
/// No per-pixel allocations.
/// </summary>
public sealed class SoftwareRenderer : IRenderer2D
{
    private uint[] _pixels;
    private int _w, _h;

    public SoftwareRenderer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _w = width; _h = height;
        _pixels = new uint[width * height];
    }

    public int ScreenWidth => _w;
    public int ScreenHeight => _h;
    /// <summary>The framebuffer, row-major, top row first.</summary>
    public uint[] Pixels => _pixels;

    public void Resize(int width, int height)
    {
        if (width == _w && height == _h) return;
        _w = width; _h = height;
        _pixels = new uint[width * height];
    }

    public void SavePng(string path) => PngWriter.Write(path, _w, _h, _pixels);

    // ---------------------------------------------------------------- core span blending

    /// <summary>Blends color over pixels [x0, x1) on row y. Clips.</summary>
    private void Span(int y, int x0, int x1, Color c)
    {
        if ((uint)y >= (uint)_h) return;
        if (x0 < 0) x0 = 0;
        if (x1 > _w) x1 = _w;
        if (x0 >= x1 || c.A == 0) return;
        var row = _pixels.AsSpan(y * _w + x0, x1 - x0);
        if (c.A == 255)
        {
            row.Fill(c.ToAbgr());
            return;
        }
        uint a = c.A, ia = 255 - a;
        uint sr = c.R * a, sg = c.G * a, sb = c.B * a;
        for (int i = 0; i < row.Length; i++)
        {
            uint d = row[i];
            uint r = (sr + (d & 0xFF) * ia + 127) / 255;
            uint g = (sg + ((d >> 8) & 0xFF) * ia + 127) / 255;
            uint b = (sb + ((d >> 16) & 0xFF) * ia + 127) / 255;
            uint da = d >> 24;
            uint oa = a + (da * ia + 127) / 255;
            row[i] = r | (g << 8) | (b << 16) | (oa << 24);
        }
    }

    private void Pixel(int x, int y, Color c) => Span(y, x, x + 1, c);

    private static int R(float v) => (int)MathF.Floor(v + 0.5f);

    // ---------------------------------------------------------------- IRenderer2D

    public void Clear(Color color) => _pixels.AsSpan().Fill(color.ToAbgr());

    public void FillRect(float x, float y, float w, float h, Color color)
    {
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        FillRectI(R(x), R(y), R(x + w), R(y + h), color);
    }

    private void FillRectI(int x0, int y0, int x1, int y1, Color c)
    {
        if (y0 < 0) y0 = 0;
        if (y1 > _h) y1 = _h;
        for (int y = y0; y < y1; y++) Span(y, x0, x1, c);
    }

    public void DrawRect(float x, float y, float w, float h, Color color, float thickness = 1f)
    {
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        int x0 = R(x), y0 = R(y), x1 = R(x + w), y1 = R(y + h);
        int t = Math.Max(1, R(thickness));
        if (x1 - x0 <= 2 * t || y1 - y0 <= 2 * t) { FillRectI(x0, y0, x1, y1, color); return; }
        FillRectI(x0, y0, x1, y0 + t, color);           // top
        FillRectI(x0, y1 - t, x1, y1, color);           // bottom
        FillRectI(x0, y0 + t, x0 + t, y1 - t, color);   // left
        FillRectI(x1 - t, y0 + t, x1, y1 - t, color);   // right
    }

    public void DrawLine(Vec2 a, Vec2 b, Color color, float thickness = 1f)
    {
        if (thickness <= 1.5f)
        {
            if (!ClipSegment(ref a, ref b, -2f, -2f, _w + 2f, _h + 2f)) return;
            Bresenham((int)MathF.Floor(a.X), (int)MathF.Floor(a.Y), (int)MathF.Floor(b.X), (int)MathF.Floor(b.Y), color);
            return;
        }
        var d = b - a;
        float len = d.Length;
        if (len < 1e-4f) { FillCircle(a, thickness * 0.5f, color); return; }
        var n = new Vec2(-d.Y / len, d.X / len) * (thickness * 0.5f);
        Span<Vec2> quad = stackalloc Vec2[4];
        quad[0] = a + n; quad[1] = b + n; quad[2] = b - n; quad[3] = a - n;
        FillConvex(quad, color);
    }

    /// <summary>Liang-Barsky clip of segment a-b to the box; false if fully outside.</summary>
    private static bool ClipSegment(ref Vec2 a, ref Vec2 b, float xmin, float ymin, float xmax, float ymax)
    {
        float t0 = 0f, t1 = 1f, dx = b.X - a.X, dy = b.Y - a.Y;
        if (!Clip(-dx, a.X - xmin, ref t0, ref t1) || !Clip(dx, xmax - a.X, ref t0, ref t1) ||
            !Clip(-dy, a.Y - ymin, ref t0, ref t1) || !Clip(dy, ymax - a.Y, ref t0, ref t1)) return false;
        var a0 = a;
        if (t1 < 1f) b = new Vec2(a0.X + t1 * dx, a0.Y + t1 * dy);
        if (t0 > 0f) a = new Vec2(a0.X + t0 * dx, a0.Y + t0 * dy);
        return true;

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (MathF.Abs(p) < 1e-9f) return q >= 0;
            float r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
    }

    private void Bresenham(int x0, int y0, int x1, int y1, Color c)
    {
        // Trivial reject if fully off one side.
        if ((x0 < 0 && x1 < 0) || (y0 < 0 && y1 < 0) || (x0 >= _w && x1 >= _w) || (y0 >= _h && y1 >= _h)) return;
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int guard = dx - dy + 2;
        while (guard-- > 0)
        {
            Pixel(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public void FillCircle(Vec2 center, float radius, Color color)
    {
        if (radius <= 0) return;
        float r2 = radius * radius;
        int yStart = Math.Max(0, (int)MathF.Floor(center.Y - radius));
        int yEnd = Math.Min(_h - 1, (int)MathF.Ceiling(center.Y + radius));
        for (int y = yStart; y <= yEnd; y++)
        {
            float dy = y + 0.5f - center.Y;
            float rem = r2 - dy * dy;
            if (rem < 0) continue;
            float half = MathF.Sqrt(rem);
            SpanF(y, center.X - half, center.X + half, color);
        }
    }

    public void DrawCircle(Vec2 center, float radius, Color color, float thickness = 1f)
    {
        if (radius <= 0) return;
        float t = MathF.Max(1f, thickness);
        float ro = radius + t * 0.5f, ri = MathF.Max(0f, radius - t * 0.5f);
        float ro2 = ro * ro, ri2 = ri * ri;
        int yStart = Math.Max(0, (int)MathF.Floor(center.Y - ro));
        int yEnd = Math.Min(_h - 1, (int)MathF.Ceiling(center.Y + ro));
        for (int y = yStart; y <= yEnd; y++)
        {
            float dy = y + 0.5f - center.Y;
            float remO = ro2 - dy * dy;
            if (remO < 0) continue;
            float ho = MathF.Sqrt(remO);
            float remI = ri2 - dy * dy;
            if (remI <= 0)
            {
                SpanF(y, center.X - ho, center.X + ho, color);
            }
            else
            {
                float hi = MathF.Sqrt(remI);
                // Left and right arcs; guarantee at least 1px each so thin rings stay closed.
                int lo0 = PxStart(center.X - ho), lo1 = Math.Max(PxStart(center.X - hi), lo0 + 1);
                int rI1 = PxEnd(center.X + ho), rI0 = Math.Min(PxEnd(center.X + hi), rI1 - 1);
                if (lo1 >= rI0) { Span(y, lo0, rI1, color); continue; }
                Span(y, lo0, lo1, color);
                Span(y, rI0, rI1, color);
            }
        }
    }

    public void FillTriangle(Vec2 a, Vec2 b, Vec2 c, Color color)
    {
        Span<Vec2> tri = stackalloc Vec2[3];
        tri[0] = a; tri[1] = b; tri[2] = c;
        FillConvex(tri, color);
    }

    /// <summary>Scanline fill of a convex polygon (any winding), pixel-center sampling.</summary>
    public void FillConvex(ReadOnlySpan<Vec2> pts, Color color)
    {
        if (pts.Length < 3) return;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in pts) { if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y; }
        int y0 = Math.Max(0, (int)MathF.Floor(minY));
        int y1 = Math.Min(_h - 1, (int)MathF.Ceiling(maxY));
        for (int y = y0; y <= y1; y++)
        {
            float yc = y + 0.5f;
            float xl = float.MaxValue, xr = float.MinValue;
            for (int i = 0; i < pts.Length; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) % pts.Length];
                float ylo = MathF.Min(p.Y, q.Y), yhi = MathF.Max(p.Y, q.Y);
                if (yc < ylo || yc >= yhi || yhi - ylo < 1e-6f) continue;
                float t = (yc - p.Y) / (q.Y - p.Y);
                float x = p.X + (q.X - p.X) * t;
                if (x < xl) xl = x;
                if (x > xr) xr = x;
            }
            if (xl <= xr) SpanF(y, xl, xr, color);
        }
    }

    // Pixels whose centers lie in [xl, xr).
    private static int PxStart(float xl) => (int)MathF.Ceiling(xl - 0.5f);
    private static int PxEnd(float xr) => (int)MathF.Ceiling(xr - 0.5f);
    private void SpanF(int y, float xl, float xr, Color c)
    {
        if (xr < -1 || xl > _w + 1) return;
        Span(y, PxStart(MathF.Max(xl, -1f)), PxEnd(MathF.Min(xr, _w + 1f)), c);
    }

    public void DrawText(float x, float y, string text, Color color, float size = 14f)
    {
        if (string.IsNullOrEmpty(text) || color.A == 0) return;
        int s = BitmapFont.ScaleFor(size);
        int ox = R(x), cx = ox, cy = R(y);
        var glyphs = BitmapFont.Glyphs;
        foreach (char ch in text)
        {
            if (ch == '\n') { cx = ox; cy += BitmapFont.LineH * s; continue; }
            int code = ch;
            if ((code < BitmapFont.First || code > BitmapFont.Last) && code != BitmapFont.MiddleDotChar) code = '?';
            if (cx >= _w) { cx += BitmapFont.CellW * s; continue; }
            if (code != ' ' && cx + BitmapFont.GlyphW * s > 0 && cy < _h && cy + BitmapFont.CellH * s > 0)
            {
                var g = code == BitmapFont.MiddleDotChar ? BitmapFont.MiddleDot : glyphs.Slice((code - BitmapFont.First) * BitmapFont.GlyphW, BitmapFont.GlyphW);
                for (int row = 0; row < BitmapFont.CellH; row++)
                {
                    // Merge consecutive set columns into one span per row.
                    int col = 0;
                    while (col < BitmapFont.GlyphW)
                    {
                        if ((g[col] & (1 << row)) == 0) { col++; continue; }
                        int start = col;
                        while (col < BitmapFont.GlyphW && (g[col] & (1 << row)) != 0) col++;
                        int px0 = cx + start * s, px1 = cx + col * s;
                        int py = cy + row * s;
                        for (int k = 0; k < s; k++) Span(py + k, px0, px1, color);
                    }
                }
            }
            cx += BitmapFont.CellW * s;
        }
    }

    public Vec2 MeasureText(string text, float size = 14f) => Measure(text, size);

    /// <summary>Static measurement (identical to <see cref="MeasureText"/>), usable without an instance.</summary>
    public static Vec2 Measure(string text, float size = 14f)
    {
        int s = BitmapFont.ScaleFor(size);
        if (string.IsNullOrEmpty(text)) return new Vec2(0, BitmapFont.CellH * s);
        int lines = 1, cur = 0, max = 0;
        foreach (char ch in text)
        {
            if (ch == '\n') { lines++; cur = 0; continue; }
            cur++;
            if (cur > max) max = cur;
        }
        float w = max == 0 ? 0 : (max * BitmapFont.CellW - 1) * s;
        float h = ((lines - 1) * BitmapFont.LineH + BitmapFont.CellH) * s;
        return new Vec2(w, h);
    }
}
