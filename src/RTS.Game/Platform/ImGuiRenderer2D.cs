using Brutal.ImGuiApi;
using Brutal.Numerics;
using RTS.Core;
using Color = RTS.Core.Color;

namespace RTS.Game.Platform;

/// <summary>
/// IRenderer2D implemented on Dear ImGui's background draw list (BRUTAL ImGui bindings).
/// Everything is recorded between ImGui.NewFrame() and ImGui.Render(); the BRUTAL
/// OpenGlBackend then rasterises the draw data. Coordinates are window (screen) pixels.
/// </summary>
public sealed class ImGuiRenderer2D : IRenderer2D
{
    private ImDrawListPtr _dl;
    private ImFontPtr _font;

    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }

    /// <summary>Clear colour requested for the current frame (used for glClear at present time).</summary>
    public Color ClearColor { get; private set; } = new(16, 18, 22);

    internal void BeginFrame(int width, int height, ImFontPtr font)
    {
        ScreenWidth = width;
        ScreenHeight = height;
        _font = font;
        _dl = ImGui.GetBackgroundDrawList();
    }

    private static ImColor8 C(Color c) => new(c.R, c.G, c.B, c.A);

    public void Clear(Color color)
    {
        ClearColor = color;
        // Cover anything already recorded this frame so Clear() mid-frame behaves like a real clear.
        var a = new float2(-1, -1);
        var b = new float2(ScreenWidth + 1, ScreenHeight + 1);
        _dl.AddRectFilled(a, b, C(color));
    }

    public void FillRect(float x, float y, float w, float h, Color color)
    {
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        var a = new float2(x, y);
        var b = new float2(x + w, y + h);
        _dl.AddRectFilled(a, b, C(color));
    }

    public void DrawRect(float x, float y, float w, float h, Color color, float thickness = 1f)
    {
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        // ImGui strokes centred on the path; offset by half a pixel so 1px lines are crisp.
        var a = new float2(x + 0.5f, y + 0.5f);
        var b = new float2(x + w - 0.5f, y + h - 0.5f);
        _dl.AddRect(a, b, C(color), 0f, ImDrawFlags.None, thickness);
    }

    public void DrawLine(Vec2 a, Vec2 b, Color color, float thickness = 1f)
    {
        var p1 = new float2(a.X, a.Y);
        var p2 = new float2(b.X, b.Y);
        _dl.AddLine(p1, p2, C(color), thickness);
    }

    public void FillCircle(Vec2 center, float radius, Color color)
    {
        if (radius <= 0) return;
        var c = new float2(center.X, center.Y);
        _dl.AddCircleFilled(c, radius, C(color));
    }

    public void DrawCircle(Vec2 center, float radius, Color color, float thickness = 1f)
    {
        if (radius <= 0) return;
        var c = new float2(center.X, center.Y);
        _dl.AddCircle(c, radius, C(color), 0, thickness);
    }

    public void FillTriangle(Vec2 a, Vec2 b, Vec2 c, Color color)
    {
        // ImGui's anti-aliased fill expects clockwise winding (screen space); fix up if needed.
        float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (cross < 0) (b, c) = (c, b);
        var p1 = new float2(a.X, a.Y);
        var p2 = new float2(b.X, b.Y);
        var p3 = new float2(c.X, c.Y);
        _dl.AddTriangleFilled(p1, p2, p3, C(color));
    }

    public void DrawText(float x, float y, string text, Color color, float size = 14f)
    {
        if (string.IsNullOrEmpty(text)) return;
        var p = new float2(x, y);
        _dl.AddText(_font, size, p, C(color), Utf8(text));
    }

    public Vec2 MeasureText(string text, float size = 14f)
    {
        if (string.IsNullOrEmpty(text)) return new Vec2(0, size);
        var s = _font.CalcTextSizeA(size, float.MaxValue, 0f, Utf8(text));
        return new Vec2(s.X, s.Y);
    }

    // ImString(ReadOnlySpan<char>) allocates from ImString.SharedStorage (a per-frame arena that
    // throws when full). Encode into our own reusable buffer instead; ImGui consumes the text
    // immediately (AddText copies glyph quads into the draw list), so reuse is safe.
    private byte[] _utf8 = new byte[256];
    private ImString Utf8(string text)
    {
        int max = System.Text.Encoding.UTF8.GetMaxByteCount(text.Length) + 1;
        if (_utf8.Length < max) _utf8 = new byte[Math.Max(max, _utf8.Length * 2)];
        int n = System.Text.Encoding.UTF8.GetBytes(text, _utf8);
        _utf8[n] = 0;
        return new ImString(_utf8.AsSpan(0, n), false);
    }
}
