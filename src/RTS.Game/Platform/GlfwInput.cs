using Brutal.GlfwApi;
using RTS.Core;

namespace RTS.Game.Platform;

/// <summary>
/// Collects GLFW input events into an <see cref="InputState"/>. Edge flags (pressed/released,
/// KeysPressed, ScrollDelta) are accumulated from callbacks between frames so very quick taps
/// are never lost, and reset by <see cref="BeginFrame"/> before events are polled.
/// </summary>
public sealed class GlfwInput
{
    private readonly GlfwWindow _window;
    public InputState State { get; } = new();

    public GlfwInput(GlfwWindow window)
    {
        _window = window;
        window.OnKey += OnKey;
        window.OnMouseButton += OnMouseButton;
        window.OnScroll += (_, offset) => State.ScrollDelta += (float)offset.Y;
        window.OnCursorEnter += (_, entered) => State.MouseInWindow = entered;
        window.OnFocus += (_, ev) =>
        {
            if (ev != GlfwFocusEvent.GainFocus) ReleaseAll();
        };
    }

    /// <summary>Call once per frame BEFORE Glfw.PollEvents().</summary>
    public void BeginFrame()
    {
        State.KeysPressed.Clear();
        State.ScrollDelta = 0;
        Array.Clear(State.MousePressed);
        Array.Clear(State.MouseReleased);
    }

    /// <summary>Call once per frame AFTER Glfw.PollEvents().</summary>
    public void EndPoll()
    {
        var pos = _window.CursorPos;
        State.MousePos = new Vec2((float)pos.X, (float)pos.Y);
    }

    private void ReleaseAll()
    {
        State.KeysDown.Clear();
        for (int i = 0; i < 3; i++)
        {
            if (State.MouseDown[i]) State.MouseReleased[i] = true;
            State.MouseDown[i] = false;
        }
    }

    private void OnMouseButton(GlfwWindow w, GlfwMouseButton button, GlfwButtonAction action, GlfwModifier mods)
    {
        int i = (int)button;
        if (i < 0 || i > 2) return;
        if (action == GlfwButtonAction.Press)
        {
            if (!State.MouseDown[i]) State.MousePressed[i] = true;
            State.MouseDown[i] = true;
        }
        else
        {
            if (State.MouseDown[i]) State.MouseReleased[i] = true;
            State.MouseDown[i] = false;
        }
    }

    private void OnKey(GlfwWindow w, GlfwKey key, int scancode, GlfwKeyAction action, GlfwModifier mods)
    {
        var k = Map(key);
        if (k == Key.Unknown) return;
        switch (action)
        {
            case GlfwKeyAction.Press:
                if (State.KeysDown.Add(k)) State.KeysPressed.Add(k);
                break;
            case GlfwKeyAction.Release:
                // Left/right modifier share one Key: keep it down while the twin is still held.
                var twin = Twin(key);
                if (twin != GlfwKey.Unknown && _window.GetKey(twin) == GlfwKeyAction.Press) break;
                State.KeysDown.Remove(k);
                break;
            // Repeat: ignored (KeysPressed is a true edge).
        }
    }

    private static GlfwKey Twin(GlfwKey key) => key switch
    {
        GlfwKey.LeftShift => GlfwKey.RightShift,
        GlfwKey.RightShift => GlfwKey.LeftShift,
        GlfwKey.LeftControl => GlfwKey.RightControl,
        GlfwKey.RightControl => GlfwKey.LeftControl,
        GlfwKey.LeftAlt => GlfwKey.RightAlt,
        GlfwKey.RightAlt => GlfwKey.LeftAlt,
        _ => GlfwKey.Unknown,
    };

    public static Key Map(GlfwKey key) => key switch
    {
        GlfwKey.Left => Key.Left,
        GlfwKey.Right => Key.Right,
        GlfwKey.Up => Key.Up,
        GlfwKey.Down => Key.Down,
        GlfwKey.W => Key.W, GlfwKey.A => Key.A, GlfwKey.S => Key.S, GlfwKey.D => Key.D,
        GlfwKey.Q => Key.Q, GlfwKey.E => Key.E, GlfwKey.R => Key.R, GlfwKey.F => Key.F,
        GlfwKey.G => Key.G, GlfwKey.H => Key.H, GlfwKey.B => Key.B, GlfwKey.T => Key.T,
        GlfwKey.X => Key.X, GlfwKey.Z => Key.Z, GlfwKey.C => Key.C, GlfwKey.V => Key.V,
        GlfwKey.P => Key.P, GlfwKey.M => Key.M,
        GlfwKey.Number0 or GlfwKey.Kp0 => Key.Num0,
        GlfwKey.Number1 or GlfwKey.Kp1 => Key.Num1,
        GlfwKey.Number2 or GlfwKey.Kp2 => Key.Num2,
        GlfwKey.Number3 or GlfwKey.Kp3 => Key.Num3,
        GlfwKey.Number4 or GlfwKey.Kp4 => Key.Num4,
        GlfwKey.Number5 or GlfwKey.Kp5 => Key.Num5,
        GlfwKey.Number6 or GlfwKey.Kp6 => Key.Num6,
        GlfwKey.Number7 or GlfwKey.Kp7 => Key.Num7,
        GlfwKey.Number8 or GlfwKey.Kp8 => Key.Num8,
        GlfwKey.Number9 or GlfwKey.Kp9 => Key.Num9,
        GlfwKey.Escape => Key.Escape,
        GlfwKey.Space => Key.Space,
        GlfwKey.Enter or GlfwKey.KpEnter => Key.Enter,
        GlfwKey.Tab => Key.Tab,
        GlfwKey.Delete or GlfwKey.Backspace => Key.Delete,
        GlfwKey.F1 => Key.F1, GlfwKey.F2 => Key.F2, GlfwKey.F3 => Key.F3, GlfwKey.F10 => Key.F10,
        GlfwKey.LeftShift or GlfwKey.RightShift => Key.LeftShift,
        GlfwKey.LeftControl or GlfwKey.RightControl => Key.LeftCtrl,
        GlfwKey.LeftAlt or GlfwKey.RightAlt => Key.LeftAlt,
        _ => Key.Unknown,
    };
}
