using RTS.Core;

namespace RTS.Snapshot;

/// <summary>Built-in test app that exercises every IRenderer2D primitive. Use with --app demo.</summary>
public sealed class ShapesTestApp : IGameApp
{
    private float _t;
    private Vec2 _mouse;
    private int _frames;

    public string Title => "Shapes Test";
    public bool WantsQuit => false;

    public void Update(InputState input, float dt)
    {
        _t += dt;
        _frames++;
        _mouse = input.MousePos;
    }

    public void Draw(IRenderer2D r)
    {
        int w = r.ScreenWidth, h = r.ScreenHeight;
        r.Clear(new Color(24, 28, 36));

        // Checker strip to make alpha blending visible.
        for (int i = 0; i < 16; i++)
            r.FillRect(20 + i * 20, 20, 20, 60, (i & 1) == 0 ? Color.White : Color.Black);
        r.FillRect(30, 30, 300, 40, Color.Red.WithAlpha(128));
        r.DrawText(36, 40, "ALPHA 50%", Color.Yellow, 16);

        // Rects
        r.FillRect(20, 100, 120, 80, Color.Blue);
        r.DrawRect(160, 100, 120, 80, Color.Green, 1);
        r.DrawRect(300, 100, 120, 80, Color.Yellow, 4);
        r.DrawRect(-40, h - 60, 100, 100, Color.Red, 3); // clipped off-screen

        // Lines (thin + thick, all directions)
        var c0 = new Vec2(540, 140);
        for (int i = 0; i < 16; i++)
        {
            float a = i * MathF.PI / 8f + _t * 0.5f;
            var dir = new Vec2(MathF.Cos(a), MathF.Sin(a));
            r.DrawLine(c0 + dir * 10, c0 + dir * 60, Color.White, i % 2 == 0 ? 1f : 3f);
        }
        r.DrawLine(new Vec2(-100, h + 50), new Vec2(w + 100, -50), Color.Gray.WithAlpha(90), 1); // clipped diagonal
        r.DrawLine(new Vec2(640, 100), new Vec2(780, 190), Color.Green.WithAlpha(160), 8f);

        // Circles
        r.FillCircle(new Vec2(80, 260), 50, Color.Green);
        r.FillCircle(new Vec2(120, 280), 40, Color.Blue.WithAlpha(140));
        r.DrawCircle(new Vec2(230, 260), 50, Color.Yellow, 1);
        r.DrawCircle(new Vec2(350, 260), 50, Color.Red, 6);
        r.FillCircle(new Vec2(w - 10, h - 10), 60, Color.Yellow.WithAlpha(100)); // clipped corner
        for (int i = 1; i <= 6; i++) r.FillCircle(new Vec2(440 + i * 22, 260), i * 1.5f, Color.White);

        // Triangles (both windings)
        r.FillTriangle(new Vec2(620, 220), new Vec2(700, 320), new Vec2(560, 320), Color.Red);
        r.FillTriangle(new Vec2(720, 220), new Vec2(800, 320), new Vec2(860, 230), new Color(200, 100, 255, 180));

        // Text at various sizes + MeasureText box
        float y = 350;
        foreach (float size in new[] { 8f, 14f, 24f, 32f })
        {
            string s = $"Size {size}: The quick brown fox jumps over the lazy dog 0123456789";
            var m = r.MeasureText(s, size);
            r.DrawRect(18, y - 2, m.X + 4, m.Y + 4, Color.Gray, 1);
            r.DrawText(20, y, s, Color.White, size);
            y += m.Y + 12;
        }
        r.DrawText(20, y, "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~  ABCDEFGHIJKLMNOPQRSTUVWXYZ", Color.Yellow, 16);
        y += 24;
        r.DrawText(20, y, "Multi-line\ntext block", Color.Green, 16);

        // HUD-ish panel with translucent background
        r.FillRect(w - 260, 20, 240, 70, new Color(0, 0, 0, 160));
        r.DrawRect(w - 260, 20, 240, 70, Color.Gray);
        r.DrawText(w - 250, 30, $"Frame {_frames}", Color.White, 16);
        r.DrawText(w - 250, 52, $"Mouse {_mouse.X:0},{_mouse.Y:0}", Color.White, 16);

        // Mouse cursor
        r.FillTriangle(_mouse, _mouse + new Vec2(0, 16), _mouse + new Vec2(11, 11), Color.White);
    }
}
