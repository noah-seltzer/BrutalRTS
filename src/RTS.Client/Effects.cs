using RTS.Core;

namespace RTS.Client;

/// <summary>Client-side, purely cosmetic effects in world space (tracers, explosions, click markers).</summary>
public sealed class Effects
{
    struct Particle { public Vec2 Pos, Vel; public float Life, Max, Size; public Color Col; }
    struct Tracer { public Vec2 A, B; public float Life, Max; public Color Col; }
    struct Ring { public Vec2 Pos; public float Life, Max, R0, R1; public Color Col; public bool Cross; }

    readonly List<Particle> _particles = new();
    readonly List<Tracer> _tracers = new();
    readonly List<Ring> _rings = new();
    readonly Random _rng = new(1234);

    public int Count => _particles.Count + _tracers.Count + _rings.Count;
    const int MaxParticles = 2000;

    float Rnd(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    public void Shot(Vec2 from, Vec2 to, Color col)
    {
        _tracers.Add(new Tracer { A = from, B = to, Life = 0.12f, Max = 0.12f, Col = col });
        if (_particles.Count < MaxParticles)
            _particles.Add(new Particle { Pos = to, Vel = new Vec2(Rnd(-20, 20), Rnd(-20, 20)), Life = 0.2f, Max = 0.2f, Size = 2.5f, Col = new Color(255, 230, 150) });
    }

    public void Explosion(Vec2 pos, float scale = 1f)
    {
        _rings.Add(new Ring { Pos = pos, Life = 0.45f, Max = 0.45f, R0 = 4 * scale, R1 = 34 * scale, Col = new Color(255, 190, 80) });
        int n = (int)(16 * scale);
        for (int i = 0; i < n && _particles.Count < MaxParticles; i++)
        {
            float a = Rnd(0, MathF.Tau), sp = Rnd(30, 140) * scale;
            float life = Rnd(0.3f, 0.8f);
            _particles.Add(new Particle
            {
                Pos = pos, Vel = new Vec2(MathF.Cos(a) * sp, MathF.Sin(a) * sp), Life = life, Max = life,
                Size = Rnd(2f, 5f) * scale,
                Col = i % 3 == 0 ? new Color(90, 85, 80) : new Color(255, (byte)Rnd(120, 220), 60),
            });
        }
    }

    public void Marker(Vec2 pos, Color col)
        => _rings.Add(new Ring { Pos = pos, Life = 0.6f, Max = 0.6f, R0 = 18, R1 = 4, Col = col, Cross = true });

    public void Ping(Vec2 pos, Color col)
        => _rings.Add(new Ring { Pos = pos, Life = 1.0f, Max = 1.0f, R0 = 10, R1 = 60, Col = col });

    public void Update(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0) { _particles.RemoveAt(i); continue; }
            p.Pos += p.Vel * dt;
            p.Vel *= 1f - MathF.Min(1f, 3f * dt);
            _particles[i] = p;
        }
        for (int i = _tracers.Count - 1; i >= 0; i--)
        {
            var t = _tracers[i]; t.Life -= dt;
            if (t.Life <= 0) _tracers.RemoveAt(i); else _tracers[i] = t;
        }
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var g = _rings[i]; g.Life -= dt;
            if (g.Life <= 0) _rings.RemoveAt(i); else _rings[i] = g;
        }
    }

    public void Draw(IRenderer2D r, Camera cam)
    {
        var view = cam.View;
        bool OnScreen(Vec2 s, float m) => s.X > view.X - m && s.X < view.Right + m && s.Y > view.Y - m && s.Y < view.Bottom + m;

        foreach (var t in _tracers)
        {
            float k = t.Life / t.Max;
            var a = cam.WorldToScreen(t.A); var b = cam.WorldToScreen(t.B);
            if (!OnScreen(a, 400) && !OnScreen(b, 400)) continue;
            r.DrawLine(a, b, Pal.Fade(t.Col, k), MathF.Max(1f, 2f * cam.Zoom));
        }
        foreach (var g in _rings)
        {
            var s = cam.WorldToScreen(g.Pos);
            if (!OnScreen(s, 100)) continue;
            float k = g.Life / g.Max;
            float rad = (g.R1 + (g.R0 - g.R1) * k) * cam.Zoom;
            var col = Pal.Fade(g.Col, k);
            r.DrawCircle(s, rad, col, 2f);
            if (g.Cross)
            {
                float c = 5 * cam.Zoom;
                r.DrawLine(new Vec2(s.X - c, s.Y - c), new Vec2(s.X + c, s.Y + c), col, 2f);
                r.DrawLine(new Vec2(s.X - c, s.Y + c), new Vec2(s.X + c, s.Y - c), col, 2f);
            }
        }
        foreach (var p in _particles)
        {
            var s = cam.WorldToScreen(p.Pos);
            if (!OnScreen(s, 20)) continue;
            float k = p.Life / p.Max;
            r.FillCircle(s, MathF.Max(0.75f, p.Size * cam.Zoom * (0.5f + 0.5f * k)), Pal.Fade(p.Col, k));
        }
    }
}
