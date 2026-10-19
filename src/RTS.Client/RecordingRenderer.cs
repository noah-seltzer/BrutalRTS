using RTS.Core;

namespace RTS.Client;

/// <summary>
/// Headless IRenderer2D for smoke tests: counts draw calls, optionally records text,
/// and flags non-finite coordinates (a common symptom of camera/math bugs).
/// </summary>
public sealed class RecordingRenderer : IRenderer2D
{
    public RecordingRenderer(int width = 1280, int height = 720) { ScreenWidth = width; ScreenHeight = height; }

    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }

    public int Clears, Rects, RectOutlines, Lines, Circles, CircleOutlines, Triangles, Texts, NonFinite;
    public int Total => Clears + Rects + RectOutlines + Lines + Circles + CircleOutlines + Triangles + Texts;
    public bool RecordText = true;
    public readonly List<string> TextLog = new();
    public Color LastClear;

    public void Reset()
    {
        Clears = Rects = RectOutlines = Lines = Circles = CircleOutlines = Triangles = Texts = NonFinite = 0;
        TextLog.Clear();
    }

    public bool ContainsText(string s) => TextLog.Any(t => t.Contains(s, StringComparison.OrdinalIgnoreCase));

    void Check(params float[] v) { foreach (var f in v) if (!float.IsFinite(f)) { NonFinite++; return; } }

    public void Clear(Color color) { Clears++; LastClear = color; }
    public void FillRect(float x, float y, float w, float h, Color color) { Rects++; Check(x, y, w, h); }
    public void DrawRect(float x, float y, float w, float h, Color color, float thickness = 1f) { RectOutlines++; Check(x, y, w, h); }
    public void DrawLine(Vec2 a, Vec2 b, Color color, float thickness = 1f) { Lines++; Check(a.X, a.Y, b.X, b.Y); }
    public void FillCircle(Vec2 center, float radius, Color color) { Circles++; Check(center.X, center.Y, radius); }
    public void DrawCircle(Vec2 center, float radius, Color color, float thickness = 1f) { CircleOutlines++; Check(center.X, center.Y, radius); }
    public void FillTriangle(Vec2 a, Vec2 b, Vec2 c, Color color) { Triangles++; Check(a.X, a.Y, b.X, b.Y, c.X, c.Y); }
    public void DrawText(float x, float y, string text, Color color, float size = 14f)
    {
        Texts++; Check(x, y);
        if (RecordText && TextLog.Count < 5000) TextLog.Add(text);
    }
    public Vec2 MeasureText(string text, float size = 14f) => new(text.Length * size * 0.55f, size);
}
