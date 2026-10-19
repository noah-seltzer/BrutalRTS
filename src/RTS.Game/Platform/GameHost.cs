using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using RTS.Core;
using Color = RTS.Core.Color;

namespace RTS.Game.Platform;

public sealed class GameHostOptions
{
    public int Width { get; init; } = 1600;
    public int Height { get; init; } = 900;
    public bool VSync { get; init; } = true;
    public bool ShowSplash { get; init; } = true;
    /// <summary>If &gt; 0, close automatically after this many seconds (smoke testing).</summary>
    public double AutoQuitSeconds { get; init; }
    /// <summary>If set, save a BMP of the splash (at 1s) and of the final auto-quit frame (debug aid).</summary>
    public string? ScreenshotPath { get; init; }
}

/// <summary>
/// Owns the BRUTAL platform stack: GLFW window + OpenGL 4.1 core context (Brutal.Glfw / Brutal.OpenGl),
/// Dear ImGui with BRUTAL's GlfwBackend + OpenGlBackend (Brutal.ImGui.Backends), and runs the main loop:
/// poll events -> build InputState -> app.Update(input, dt) -> app.Draw(renderer) -> present.
/// </summary>
public sealed class GameHost : IDisposable
{
    private readonly IGameApp _app;
    private readonly GameHostOptions _opt;
    private GlfwWindow? _window;
    private GlfwBackend? _platformBackend;
    private OpenGlBackend? _rendererBackend;
    private GlfwInput? _input;
    private readonly ImGuiRenderer2D _renderer = new();
    private ImFontPtr _font;
    private static GlGetProcAddressFunc? s_loader; // keep the delegate rooted

    public GameHost(IGameApp app, GameHostOptions? options = null)
    {
        _app = app;
        _opt = options ?? new GameHostOptions();
    }

    public void Run()
    {
        Init();
        Loop();
    }

    private unsafe void Init()
    {
        Glfw.ErrorCallback += (code, desc) => Log.Error($"GLFW error {code}: {desc}");
        if (!Glfw.Init()) throw new InvalidOperationException("Glfw.Init failed");
        Log.Info($"GLFW {Glfw.VersionString}");

        Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 4);
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 1);
        Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, 0x00032001); // GLFW_OPENGL_CORE_PROFILE
        Glfw.WindowHint(GlfwWindowHint.OpenGlForwardCompat, 1);
        Glfw.WindowHint(GlfwWindowHint.Resizable, 1);
        Glfw.WindowHint(GlfwWindowHint.ScaleToMonitor, 1);
        Glfw.WindowHint(GlfwWindowHint.Visible, 0);

        var ci = new GlfwWindow.CreateInfo { Title = _app.Title, Size = new int2(_opt.Width, _opt.Height) };
        _window = Glfw.CreateWindow(in ci);
        if (_window is null || _window.IsNull()) throw new InvalidOperationException("Glfw.CreateWindow failed (OpenGL 4.1 core unavailable?)");
        _window.CenterToMonitor(Glfw.PrimaryMonitor);
        _window.SetSizeLimits(new int2(640, 360), new int2(-1, -1)); // GLFW_DONT_CARE
        _window.Visible = true;
        _window.MakeContextCurrent();
        Glfw.SwapInterval(_opt.VSync ? 1 : 0);

        s_loader = name => Glfw.GetProcAddress(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(name));
        if (!Glad.Initialize(s_loader)) throw new InvalidOperationException("Glad.Initialize failed");
        Log.Info($"OpenGL {Marshal.PtrToStringUTF8((nint)OpenGl.GetString(0x1F02))} / {Marshal.PtrToStringUTF8((nint)OpenGl.GetString(0x1F01))}");

        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.IniFilename = default; // no imgui.ini
        io.LogFilename = default;
        ImGui.StyleColorsDark();
        _font = LoadFont(io);

        _platformBackend = new GlfwBackend(_window, GlfwBackend.ClientApi.OpenGL);
        _rendererBackend = new OpenGlBackend();
        _input = new GlfwInput(_window);
        Log.Info($"Window {_window.Size.X}x{_window.Size.Y}, framebuffer {_window.FramebufferSize.X}x{_window.FramebufferSize.Y}");
    }

    private static ImFontPtr LoadFont(ImGuiIOPtr io)
    {
        string winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var name in new[] { "segoeui.ttf", "arial.ttf", "tahoma.ttf" })
        {
            var path = Path.Combine(winFonts, name);
            if (!File.Exists(path)) continue;
            var f = io.Fonts.AddFontFromFileTTF(new ImString(path.AsSpan()), 18f);
            if (!f.IsNull()) { Log.Info($"Font: {path}"); io.FontDefault = f; return f; }
        }
        Log.Info("Font: ImGui default (ProggyClean)");
        return io.Fonts.AddFontDefault();
    }

    private void Loop()
    {
        var window = _window!;
        var input = _input!;
        double start = Glfw.Time, last = start;
        double splashEnd = _opt.ShowSplash ? SplashScreen.Duration : 0;
        double appStart = -1;
        long frames = 0;
        bool loggedStats = false, shotSplash = false;
        int2 lastFb = window.FramebufferSize;

        while (!window.ShouldClose && !_app.WantsQuit)
        {
            input.BeginFrame();
            Glfw.PollEvents();
            input.EndPoll();

            var fb = window.FramebufferSize;
            if (window.IsIconified || fb.X <= 0 || fb.Y <= 0)
            {
                Glfw.WaitEventsTimeout(0.1); // minimised: idle, don't render
                last = Glfw.Time;
                continue;
            }
            if (fb.X != lastFb.X || fb.Y != lastFb.Y)
            {
                Log.Info($"Resize: framebuffer {fb.X}x{fb.Y}, window {window.Size.X}x{window.Size.Y}");
                lastFb = fb;
            }

            double now = Glfw.Time;
            float dt = (float)Math.Min(now - last, 0.1);
            last = now;
            double t = now - start;

            _rendererBackend!.NewFrame();
            _platformBackend!.NewFrame();
            ImGui.NewFrame();
            var size = window.Size;
            _renderer.BeginFrame(size.X, size.Y, _font);

            if (t < splashEnd)
            {
                SplashScreen.Draw(_renderer, (float)t);
            }
            else
            {
                if (appStart < 0) { appStart = now; Log.Info("Splash finished, starting app"); }
                _app.Update(input.State, dt);
                _app.Draw(_renderer);
            }

            ImGui.Render();
            var cc = _renderer.ClearColor;
            OpenGl.Viewport(0, 0, fb.X, fb.Y);
            OpenGl.ClearColor(new float4(cc.R / 255f, cc.G / 255f, cc.B / 255f, 1f));
            OpenGl.Clear(GlClearBit.Color);
            _rendererBackend.RenderDrawData(ImGui.GetDrawData());
            bool quitNow = _opt.AutoQuitSeconds > 0 && t >= _opt.AutoQuitSeconds;
            if (_opt.ScreenshotPath is { } shot && (quitNow || (!shotSplash && t >= 1.0 && t < splashEnd)))
            {
                var path = quitNow ? shot : Path.ChangeExtension(shot, null) + "-splash.bmp";
                SaveBackbuffer(path, fb.X, fb.Y);
                shotSplash = true;
            }
            window.SwapBuffers();
            ImString.SharedStorage.Reset(); // per-frame ImString arena (throws when full if never reset)
            frames++;

            if (!loggedStats && t >= 3.0)
            {
                loggedStats = true;
                Log.Info($"Stats after {t:F1}s: {frames} frames ({frames / t:F0} fps avg), screen {size.X}x{size.Y}, fb {fb.X}x{fb.Y}, managed heap {GC.GetTotalMemory(false) / 1024} KB");
            }
            if (quitNow)
            {
                Log.Info($"Auto-quit after {t:F1}s, {frames} frames, managed heap {GC.GetTotalMemory(false) / 1024} KB");
                break;
            }
        }
        Log.Info($"Main loop exited (frames={frames}, close={window.ShouldClose}, wantsQuit={_app.WantsQuit})");
    }

    /// <summary>Debug aid: read the GL back buffer and write a 32-bit BMP (bottom-up rows = GL order).</summary>
    private static unsafe void SaveBackbuffer(string path, int w, int h)
    {
        var px = new byte[w * h * 4];
        OpenGl.ReadBuffer(0x0405); // GL_BACK
        fixed (byte* p = px) OpenGl.ReadPixels(0, 0, w, h, 0x80E1 /*GL_BGRA*/, 0x1401 /*GL_UNSIGNED_BYTE*/, p);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(54 + px.Length); bw.Write(0); bw.Write(54);
        bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(px.Length); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
        bw.Write(px);
        Log.Info($"Screenshot saved: {path}");
    }

    public void Dispose()
    {
        _rendererBackend?.Dispose();
        _platformBackend?.Dispose();
        if (_window is not null) ImGui.DestroyContext();
        _window?.Dispose();
        Glfw.Terminate();
    }
}
