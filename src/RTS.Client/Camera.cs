using RTS.Core;

namespace RTS.Client;

/// <summary>World-to-screen transform. Center = world point shown in the middle of the play area.</summary>
public sealed class Camera
{
    public const float MinZoom = 0.5f, MaxZoom = 2f, PanSpeed = 900f;

    public Vec2 Center;
    public float Zoom = 1f;
    public RectF View = new(0, 0, 1280, 720);
    public float MapW = 1, MapH = 1;

    bool _midDrag;
    Vec2 _midStartMouse, _midStartCenter;

    public Vec2 WorldToScreen(Vec2 w)
        => new((w.X - Center.X) * Zoom + View.X + View.W / 2, (w.Y - Center.Y) * Zoom + View.Y + View.H / 2);

    public Vec2 ScreenToWorld(Vec2 s)
        => new((s.X - View.X - View.W / 2) / Zoom + Center.X, (s.Y - View.Y - View.H / 2) / Zoom + Center.Y);

    /// <summary>World-space rectangle currently visible.</summary>
    public RectF WorldView
    {
        get
        {
            var a = ScreenToWorld(new Vec2(View.X, View.Y));
            return new(a.X, a.Y, View.W / Zoom, View.H / Zoom);
        }
    }

    public void CenterOn(Vec2 w) { Center = w; Clamp(); }

    public void ZoomAt(Vec2 screen, float factor)
    {
        var before = ScreenToWorld(screen);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var after = ScreenToWorld(screen);
        Center += before - after;
        Clamp();
    }

    public void Clamp()
    {
        float hw = View.W / 2 / Zoom, hh = View.H / 2 / Zoom;
        float x = MapW <= hw * 2 ? MapW / 2 : Math.Clamp(Center.X, hw, MapW - hw);
        float y = MapH <= hh * 2 ? MapH / 2 : Math.Clamp(Center.Y, hh, MapH - hh);
        Center = new Vec2(x, y);
    }

    public bool IsEdgeScrolling(Vec2 m, Layout L, out Vec2 dir)
    {
        float dx = 0, dy = 0;
        var play = L.PlayArea;
        bool inWindow = m.X >= 0 && m.Y >= 0 && m.X < L.W && m.Y < L.H;
        if (inWindow)
        {
            // Horizontal edges count anywhere alongside the play area (incl. top bar).
            if (m.Y < play.Bottom)
            {
                if (m.X < Layout.EdgeScroll) dx -= 1;
                if (m.X >= L.W - Layout.EdgeScroll) dx += 1;
            }
            // Top: includes top bar.
            if (m.Y < play.Y + Layout.EdgeScroll) dy -= 1;
            // Bottom: strip ABOVE the HUD panel (the HUD covers the real screen edge) + the very screen bottom.
            if ((m.Y >= play.Bottom - Layout.EdgeScroll && m.Y < play.Bottom) || m.Y >= L.H - 2) dy += 1;
        }
        dir = new Vec2(dx, dy);
        return dx != 0 || dy != 0;
    }

    public void Update(InputState input, float dt, Layout L, bool allowMouse)
    {
        View = L.PlayArea;
        float dx = 0, dy = 0;
        if (input.Down(Key.Left)) dx -= 1;
        if (input.Down(Key.Right)) dx += 1;
        if (input.Down(Key.Up)) dy -= 1;
        if (input.Down(Key.Down)) dy += 1;
        if (allowMouse && input.MouseInWindow && !_midDrag && IsEdgeScrolling(input.MousePos, L, out var e))
        {
            dx += e.X; dy += e.Y;
        }
        if (dx != 0 || dy != 0)
        {
            var d = new Vec2(Math.Clamp(dx, -1, 1), Math.Clamp(dy, -1, 1));
            Center += d * (PanSpeed / Zoom * dt);
        }

        if (allowMouse && input.ScrollDelta != 0 && View.Contains(input.MousePos))
            ZoomAt(input.MousePos, MathF.Pow(1.15f, input.ScrollDelta));

        if (input.MousePressed[(int)MouseButton.Middle] && View.Contains(input.MousePos))
        {
            _midDrag = true; _midStartMouse = input.MousePos; _midStartCenter = Center;
        }
        if (_midDrag)
        {
            if (!input.MouseDown[(int)MouseButton.Middle]) _midDrag = false;
            else Center = _midStartCenter - (input.MousePos - _midStartMouse) / Zoom;
        }
        Clamp();
    }
}
