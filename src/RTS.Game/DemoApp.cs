using RTS.Core;

namespace RTS.Game;

/// <summary>Platform-layer test app: exercises every IRenderer2D method and shows live input state.</summary>
public sealed class DemoApp : IGameApp
{
    private Vec2 _ball = new(400, 300);
    private float _time;
    private float _zoom = 1f;
    private bool _quit;
    private bool _showCredits;
    private readonly List<string> _events = new();

    public string Title => "BrutalRTS - Platform Demo";
    public bool WantsQuit => _quit;

    public void Update(InputState input, float dt)
    {
        _time += dt;
        // Ball eases towards the mouse.
        _ball += (input.MousePos - _ball) * MathF.Min(1f, dt * 8f);
        _zoom = Math.Clamp(_zoom + input.ScrollDelta * 0.1f, 0.3f, 3f);
        if (input.Pressed(Key.Escape)) _quit = true;
        if (input.Pressed(Key.F1)) _showCredits = !_showCredits;
        foreach (var k in input.KeysPressed) Push($"key pressed {k}");
        for (int i = 0; i < 3; i++)
        {
            if (input.MousePressed[i]) Push($"mouse {(MouseButton)i} pressed @ {input.MousePos.X:F0},{input.MousePos.Y:F0}");
            if (input.MouseReleased[i]) Push($"mouse {(MouseButton)i} released");
        }
        if (input.ScrollDelta != 0) Push($"scroll {input.ScrollDelta:+0.0;-0.0}");
        _lastInput = input;
    }

    private InputState? _lastInput;

    private void Push(string s)
    {
        _events.Add($"{_time,7:F2}  {s}");
        if (_events.Count > 12) _events.RemoveAt(0);
    }

    public void Draw(IRenderer2D r)
    {
        r.Clear(new Color(24, 28, 34));
        int w = r.ScreenWidth, h = r.ScreenHeight;

        // Grid lines.
        for (int x = 0; x < w; x += 50) r.DrawLine(new Vec2(x, 0), new Vec2(x, h), new Color(40, 46, 56));
        for (int y = 0; y < h; y += 50) r.DrawLine(new Vec2(0, y), new Vec2(w, y), new Color(40, 46, 56));

        // Shapes showcase.
        r.FillRect(60, 120, 120, 80, Color.Blue);
        r.DrawRect(200, 120, 120, 80, Color.Yellow, 3f);
        r.FillCircle(new Vec2(400, 160), 40, Color.Green);
        r.DrawCircle(new Vec2(500, 160), 40, Color.Red, 2f);
        r.FillTriangle(new Vec2(560, 200), new Vec2(620, 120), new Vec2(680, 200), new Color(200, 120, 255));
        r.FillTriangle(new Vec2(700, 120), new Vec2(760, 200), new Vec2(820, 120), new Color(255, 160, 60)); // CCW input
        r.FillRect(840, 120, 120, 80, Color.White.WithAlpha(90)); // alpha
        float sx = 1000 + MathF.Sin(_time * 2) * 80;
        r.DrawLine(new Vec2(1000, 160), new Vec2(sx + 80, 160 + MathF.Cos(_time * 2) * 40), Color.White, 4f);

        // Text sizes + measurement box.
        float ty = 240;
        foreach (var sz in new[] { 12f, 14f, 20f, 32f, 48f })
        {
            string s = $"Text {sz:F0}px - The quick brown fox";
            var m = r.MeasureText(s, sz);
            r.DrawRect(60, ty, m.X, m.Y, new Color(90, 90, 90));
            r.DrawText(60, ty, s, Color.White, sz);
            ty += m.Y + 6;
        }

        // Mouse follower.
        r.FillCircle(_ball, 18 * _zoom, Color.Red.WithAlpha(200));
        r.DrawCircle(_ball, 24 * _zoom, Color.White, 2f);

        // Input readout.
        if (_lastInput is { } inp)
        {
            float x = w - 460, y = 240;
            r.FillRect(x - 10, y - 10, 450, 420, new Color(0, 0, 0, 160));
            r.DrawText(x, y, $"Screen {w}x{h}   Mouse {inp.MousePos.X:F0},{inp.MousePos.Y:F0}  in={inp.MouseInWindow}", Color.Yellow, 16); y += 22;
            r.DrawText(x, y, $"Buttons L={inp.MouseDown[0]} R={inp.MouseDown[1]} M={inp.MouseDown[2]}  zoom={_zoom:F1}", Color.Yellow, 16); y += 22;
            r.DrawText(x, y, "Keys down: " + string.Join(" ", inp.KeysDown), Color.Yellow, 16); y += 30;
            foreach (var e in _events) { r.DrawText(x, y, e, Color.White, 14); y += 18; }
        }

        if (_showCredits)
        {
            float y = h - 30 - Credits.Lines.Length * 20;
            r.FillRect(40, y - 10, 900, Credits.Lines.Length * 20 + 20, new Color(0, 0, 0, 200));
            foreach (var line in Credits.Lines) { r.DrawText(50, y, line, Color.White, 16); y += 20; }
        }

        r.DrawText(20, 20, "BrutalRTS platform demo", Color.White, 32);
        r.DrawText(20, 62, "Move mouse / click / scroll / type.  F1 = credits, Esc = quit", Color.Gray, 16);
    }
}
