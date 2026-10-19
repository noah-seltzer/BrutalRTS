// Shared contracts between the platform layer (RTS.Game, BRUTAL-specific),
// the client layer (RTS.Client: camera/UI/input mapping) and the simulation (RTS.Sim).
// RTS.Core has NO dependencies. Do not change these signatures without updating all layers.
namespace RTS.Core;

public readonly record struct Vec2(float X, float Y)
{
    public static readonly Vec2 Zero = new(0, 0);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, float s) => new(a.X / s, a.Y / s);
    public float Length => MathF.Sqrt(X * X + Y * Y);
    public float LengthSq => X * X + Y * Y;
    public Vec2 Normalized => Length > 1e-6f ? this / Length : Zero;
    public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static float DistanceSq(Vec2 a, Vec2 b) => (a - b).LengthSq;
    // Record's generated ToString would recurse through Normalized (also a Vec2).
    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}

/// <summary>RGBA, 0..255 per channel.</summary>
public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Red = new(220, 60, 60);
    public static readonly Color Green = new(60, 200, 90);
    public static readonly Color Blue = new(70, 130, 230);
    public static readonly Color Yellow = new(240, 210, 60);
    public static readonly Color Gray = new(128, 128, 128);
    public Color WithAlpha(byte a) => this with { A = a };
    public uint ToAbgr() => (uint)(R | (G << 8) | (B << 16) | (A << 24)); // ImGui IM_COL32 layout
}

/// <summary>
/// Immediate-mode 2D renderer. All coordinates are SCREEN pixels, origin top-left, +Y down.
/// The platform layer implements this on top of BRUTAL (Vulkan / ImGui draw lists).
/// </summary>
public interface IRenderer2D
{
    int ScreenWidth { get; }
    int ScreenHeight { get; }
    void Clear(Color color);
    void FillRect(float x, float y, float w, float h, Color color);
    void DrawRect(float x, float y, float w, float h, Color color, float thickness = 1f);
    void DrawLine(Vec2 a, Vec2 b, Color color, float thickness = 1f);
    void FillCircle(Vec2 center, float radius, Color color);
    void DrawCircle(Vec2 center, float radius, Color color, float thickness = 1f);
    void FillTriangle(Vec2 a, Vec2 b, Vec2 c, Color color);
    /// <summary>Draws text with its top-left at (x,y). size = pixel height (approximate is fine).</summary>
    void DrawText(float x, float y, string text, Color color, float size = 14f);
    Vec2 MeasureText(string text, float size = 14f);
}

public enum MouseButton { Left = 0, Right = 1, Middle = 2 }

/// <summary>Subset of keys the game uses. Platform maps GLFW key codes to these.</summary>
public enum Key
{
    Unknown = 0,
    Left, Right, Up, Down,
    W, A, S, D, Q, E, R, F, G, H, B, T, X, Z, C, V, P, M,
    Num0, Num1, Num2, Num3, Num4, Num5, Num6, Num7, Num8, Num9,
    Escape, Space, Enter, Tab, Delete, F1, F2, F3, F10,
    LeftShift, LeftCtrl, LeftAlt,
}

/// <summary>Per-frame input snapshot built by the platform layer.</summary>
public sealed class InputState
{
    public Vec2 MousePos;                 // screen pixels
    public float ScrollDelta;             // +up / -down, wheel notches this frame
    public readonly bool[] MouseDown = new bool[3];
    public readonly bool[] MousePressed = new bool[3];  // went down this frame
    public readonly bool[] MouseReleased = new bool[3]; // went up this frame
    public readonly HashSet<Key> KeysDown = new();
    public readonly HashSet<Key> KeysPressed = new();   // went down this frame
    public bool MouseInWindow = true;

    public bool Down(Key k) => KeysDown.Contains(k);
    public bool Pressed(Key k) => KeysPressed.Contains(k);
    public bool Shift => Down(Key.LeftShift);
    public bool Ctrl => Down(Key.LeftCtrl);
}

/// <summary>
/// What the platform layer drives. RTS.Client provides the implementation (RTS.Client.GameClient).
/// Loop: platform polls events -> builds InputState -> Update(input, dt) -> Draw(renderer) -> present.
/// </summary>
public interface IGameApp
{
    string Title { get; }
    void Update(InputState input, float dt);
    void Draw(IRenderer2D r);
    bool WantsQuit { get; }
}
