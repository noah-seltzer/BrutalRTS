using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

/// <summary>Primitive shapes shared by the world view and HUD icons.</summary>
public static class Shapes
{
    public static void Unit(IRenderer2D r, EntityKind kind, Vec2 c, float rad, Color fill, Color outline, Vec2 facing)
    {
        var f = facing.LengthSq > 1e-6f ? facing.Normalized : new Vec2(1, 0);
        var p = new Vec2(-f.Y, f.X);
        switch (kind)
        {
            case EntityKind.Worker:
                r.FillCircle(c, rad, fill);
                r.DrawCircle(c, rad, outline, 1.5f);
                break;
            case EntityKind.Soldier:
            {
                var a = c + f * (rad * 1.25f);
                var b = c - f * (rad * 0.85f) + p * rad;
                var d = c - f * (rad * 0.85f) - p * rad;
                r.FillTriangle(a, b, d, fill);
                r.DrawLine(a, b, outline, 1.5f); r.DrawLine(b, d, outline, 1.5f); r.DrawLine(d, a, outline, 1.5f);
                break;
            }
            case EntityKind.Ranger:
            {
                var t = c + f * (rad * 1.3f);
                var bk = c - f * (rad * 1.0f);
                var l = c + p * (rad * 0.85f);
                var rr = c - p * (rad * 0.85f);
                r.FillTriangle(t, l, rr, fill);
                r.FillTriangle(bk, l, rr, fill);
                r.DrawLine(t, l, outline, 1.5f); r.DrawLine(l, bk, outline, 1.5f);
                r.DrawLine(bk, rr, outline, 1.5f); r.DrawLine(rr, t, outline, 1.5f);
                break;
            }
            case EntityKind.Tank:
                r.FillRect(c.X - rad, c.Y - rad, rad * 2, rad * 2, fill);
                r.DrawRect(c.X - rad, c.Y - rad, rad * 2, rad * 2, outline, 1.5f);
                r.FillCircle(c, rad * 0.45f, Pal.Scale(fill, 0.7f));
                r.DrawLine(c, c + f * (rad * 1.6f), outline, MathF.Max(2f, rad * 0.25f));
                break;
            default:
                r.FillCircle(c, rad, fill);
                break;
        }
    }

    /// <summary>Small structure glyph drawn inside a footprint square (or as an icon).</summary>
    public static void StructureGlyph(IRenderer2D r, EntityKind kind, RectF rc, Color c, Vec2 facing)
    {
        var ctr = rc.Center;
        float s = rc.W;
        switch (kind)
        {
            case EntityKind.HQ:
                r.DrawRect(rc.X + s * 0.25f, rc.Y + s * 0.25f, s * 0.5f, s * 0.5f, c, 2f);
                r.FillRect(rc.X + s * 0.4f, rc.Y + s * 0.4f, s * 0.2f, s * 0.2f, c);
                break;
            case EntityKind.Barracks:
                for (int i = 0; i < 3; i++)
                    r.FillRect(rc.X + s * (0.18f + i * 0.24f), rc.Y + s * 0.3f, s * 0.16f, s * 0.4f, c);
                break;
            case EntityKind.Factory:
                r.DrawCircle(ctr, s * 0.25f, c, 2f);
                r.FillRect(rc.X + s * 0.65f, rc.Y + s * 0.15f, s * 0.12f, s * 0.3f, c);
                break;
            case EntityKind.SupplyDepot:
                for (int i = 0; i < 3; i++)
                    r.FillRect(rc.X + s * 0.2f, rc.Y + s * (0.22f + i * 0.2f), s * 0.6f, s * 0.1f, c);
                break;
            case EntityKind.Turret:
            {
                r.FillCircle(ctr, s * 0.22f, c);
                var f = facing.LengthSq > 1e-6f ? facing.Normalized : new Vec2(1, 0);
                r.DrawLine(ctr, ctr + f * (s * 0.42f), c, MathF.Max(2f, s * 0.08f));
                break;
            }
            default:
                r.DrawRect(rc.X + s * 0.3f, rc.Y + s * 0.3f, s * 0.4f, s * 0.4f, c, 1.5f);
                break;
        }
    }

    public static void Crystal(IRenderer2D r, Vec2 c, float size, Color col)
    {
        var top = new Vec2(c.X, c.Y - size * 1.4f);
        var bot = new Vec2(c.X, c.Y + size * 0.6f);
        var l = new Vec2(c.X - size * 0.6f, c.Y - size * 0.2f);
        var rr = new Vec2(c.X + size * 0.6f, c.Y - size * 0.2f);
        r.FillTriangle(top, l, rr, col);
        r.FillTriangle(bot, l, rr, Pal.Scale(col, 0.75f));
    }

    public static void Chevron(IRenderer2D r, Vec2 c, float w, Color col)
    {
        r.DrawLine(new Vec2(c.X - w, c.Y - w * 0.5f), c, col, 2f);
        r.DrawLine(c, new Vec2(c.X + w, c.Y - w * 0.5f), col, 2f);
    }

    /// <summary>Kind icon for HUD (unit shape or structure glyph) fitted in a box.</summary>
    public static void Icon(IRenderer2D r, EntityKind kind, RectF rc, int owner)
    {
        var col = Pal.TeamOf(owner);
        if (kind == EntityKind.CrystalField)
        {
            Crystal(r, rc.Center + new Vec2(0, rc.H * 0.15f), rc.W * 0.25f, Pal.Neutral);
            return;
        }
        if (World.DefOf(kind).IsStructure)
        {
            var inner = new RectF(rc.X + 3, rc.Y + 3, rc.W - 6, rc.H - 6);
            r.FillRect(inner.X, inner.Y, inner.W, inner.H, Pal.TeamDarkOf(owner));
            r.DrawRect(inner.X, inner.Y, inner.W, inner.H, col, 1.5f);
            StructureGlyph(r, kind, inner, col, new Vec2(1, -0.5f));
        }
        else
        {
            Unit(r, kind, rc.Center, rc.W * 0.28f, col, Color.White, new Vec2(0, -1));
        }
    }
}
