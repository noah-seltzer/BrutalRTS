using System.Diagnostics;
using System.Globalization;
using RTS.Core;
using RTS.SoftRender;

namespace RTS.Snapshot;

/// <summary>
/// Headless driver: runs an IGameApp for N frames at a fixed dt with scripted input and writes PNG
/// snapshots rendered by <see cref="SoftwareRenderer"/>.
/// </summary>
public static class Program
{
    private const string Usage = """
        RTS.Snapshot - headless frame capture
          --app client|demo     IGameApp to drive (default: client if built with it, else demo)
          --script NAME         menu | start | demo | queue   (default: menu)
          --frames N            frames to simulate at dt=1/60 (default: 300)
          --out DIR             output directory (default: ./snapshots)
          --every K             capture every K frames (default: N/5); last frame always captured
          --at a,b,c            capture exactly these frames (1-based) in addition to the last
          --width W --height H  framebuffer size (default 1280x720)
          --prefix P            file name prefix (default: <app>_<script>)
        """;

    public static int Main(string[] args)
    {
        string app = HasClient ? "client" : "demo";
        string script = "menu";
        string outDir = "snapshots";
        string? prefix = null;
        int frames = 300, width = 1280, height = 720, every = 0;
        var at = new SortedSet<int>();

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
                switch (args[i])
                {
                    case "--app": app = Next(); break;
                    case "--script": script = Next(); break;
                    case "--frames": frames = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--out": outDir = Next(); break;
                    case "--every": every = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--at":
                        foreach (var s in Next().Split(',', StringSplitOptions.RemoveEmptyEntries))
                            at.Add(int.Parse(s, CultureInfo.InvariantCulture));
                        break;
                    case "--width": width = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--height": height = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--prefix": prefix = Next(); break;
                    case "-h": case "--help": Console.WriteLine(Usage); return 0;
                    default: throw new ArgumentException($"Unknown argument '{args[i]}'");
                }
            }
            if (frames < 1) throw new ArgumentException("--frames must be >= 1");
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        IGameApp game;
        try { game = CreateApp(app); }
        catch (Exception e) { Console.Error.WriteLine($"Failed to create app '{app}': {e.Message}"); return 3; }

        if (!Scripts.TryGet(script, out var scriptFn))
        {
            Console.Error.WriteLine($"Unknown script '{script}'. Available: {string.Join(", ", Scripts.Names)}");
            return 2;
        }

        if (every <= 0 && at.Count == 0) every = Math.Max(1, frames / 5);
        prefix ??= $"{app}_{script}";
        Directory.CreateDirectory(outDir);

        var renderer = new SoftwareRenderer(width, height);
        var driver = new InputDriver(width, height);
        const float dt = 1f / 60f;
        var sw = Stopwatch.StartNew();
        int written = 0;

        Console.WriteLine($"Running '{game.Title}' ({app}) script={script} frames={frames} size={width}x{height}");
        for (int f = 1; f <= frames; f++)
        {
            var input = driver.Next(f, scriptFn);
            try
            {
                game.Update(input, dt);
                game.Draw(renderer);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Exception at frame {f}: {e}");
                Save(renderer, outDir, prefix, f, "crash");
                return 4;
            }

            bool last = f == frames || game.WantsQuit;
            if (last || at.Contains(f) || (every > 0 && f % every == 0))
            {
                Save(renderer, outDir, prefix, f, null);
                written++;
            }
            if (game.WantsQuit) { Console.WriteLine($"App requested quit at frame {f}."); break; }
        }
        Console.WriteLine($"Done: {written} snapshot(s) in {Path.GetFullPath(outDir)} ({sw.ElapsedMilliseconds} ms)");
        return 0;
    }

    private static void Save(SoftwareRenderer r, string dir, string prefix, int frame, string? tag)
    {
        string path = Path.Combine(dir, $"{prefix}_{frame:D5}{(tag is null ? "" : "_" + tag)}.png");
        r.SavePng(path);
        Console.WriteLine($"  wrote {path}");
    }

#if HAS_CLIENT
    private const bool HasClient = true;
#else
    private const bool HasClient = false;
#endif

    private static IGameApp CreateApp(string name) => name switch
    {
        "demo" or "shapes" => new ShapesTestApp(),
#if HAS_CLIENT
        "client" or "game" => new RTS.Client.GameClient(),
#else
        "client" or "game" => throw new InvalidOperationException(
            "RTS.Snapshot was built without RTS.Client (NoClient=true). Rebuild without -p:NoClient=true."),
#endif
        _ => throw new ArgumentException($"Unknown app '{name}' (use client or demo)"),
    };
}

/// <summary>Desired input for a frame; the driver derives Pressed/Released edges from consecutive frames.</summary>
public sealed class ScriptFrame
{
    public Vec2 Mouse;
    public readonly bool[] MouseDown = new bool[3];
    public readonly HashSet<Key> Keys = new();
    public float Scroll;

    public void Reset(Vec2 mouse)
    {
        Mouse = mouse; Scroll = 0;
        Array.Clear(MouseDown);
        Keys.Clear();
    }
}

/// <summary>Script: (frame index 1-based, screen size, frame to fill). Mouse defaults to the previous position.</summary>
public delegate void InputScript(int frame, int width, int height, ScriptFrame s);

public sealed class InputDriver(int width, int height)
{
    private readonly InputState _state = new();
    private readonly ScriptFrame _frame = new();
    private readonly bool[] _prevMouse = new bool[3];
    private readonly HashSet<Key> _prevKeys = new();
    private Vec2 _mouse = new(width / 2f, height / 2f);

    public InputState Next(int frame, InputScript script)
    {
        _frame.Reset(_mouse);
        script(frame, width, height, _frame);
        _mouse = _frame.Mouse;

        _state.MousePos = _frame.Mouse;
        _state.ScrollDelta = _frame.Scroll;
        _state.MouseInWindow = true;
        for (int b = 0; b < 3; b++)
        {
            bool down = _frame.MouseDown[b];
            _state.MouseDown[b] = down;
            _state.MousePressed[b] = down && !_prevMouse[b];
            _state.MouseReleased[b] = !down && _prevMouse[b];
            _prevMouse[b] = down;
        }
        _state.KeysDown.Clear();
        _state.KeysPressed.Clear();
        foreach (var k in _frame.Keys)
        {
            _state.KeysDown.Add(k);
            if (!_prevKeys.Contains(k)) _state.KeysPressed.Add(k);
        }
        _prevKeys.Clear();
        _prevKeys.UnionWith(_frame.Keys);
        return _state;
    }
}

public static class Scripts
{
    private static readonly Dictionary<string, InputScript> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ["menu"] = static (_, _, _, _) => { },
        ["start"] = Start,
        ["demo"] = Demo,
        ["queue"] = Queue,
    };

    public static IEnumerable<string> Names => All.Keys;
    public static bool TryGet(string name, out InputScript script) => All.TryGetValue(name, out script!);

    private const int EnterFrame = 10;

    /// <summary>Press Enter on the menu (held for 3 frames), then idle.</summary>
    private static void Start(int f, int w, int h, ScriptFrame s)
    {
        if (f >= EnterFrame && f < EnterFrame + 3) s.Keys.Add(Key.Enter);
    }

    /// <summary>
    /// start, then: box-select around screen center (f60-80), right-click move order (f100),
    /// pan with arrow keys (f130-200), zoom with scroll (f220-250), then idle with mouse at center.
    /// </summary>
    private static void Demo(int f, int w, int h, ScriptFrame s)
    {
        Start(f, w, h, s);
        var c = new Vec2(w / 2f, h / 2f);
        var boxA = c + new Vec2(-180, -130);
        var boxB = c + new Vec2(180, 130);

        if (f >= 50 && f < 60) s.Mouse = boxA;
        else if (f >= 60 && f <= 80)
        {
            // Drag from boxA to boxB with the left button held; released on frame 81.
            float t = (f - 60) / 20f;
            s.Mouse = boxA + (boxB - boxA) * t;
            s.MouseDown[(int)MouseButton.Left] = true;
        }
        else if (f >= 81 && f < 100) s.Mouse = boxB;
        else if (f >= 100 && f < 130)
        {
            s.Mouse = c + new Vec2(w * 0.25f, -h * 0.15f);
            if (f < 103) s.MouseDown[(int)MouseButton.Right] = true;
        }
        else if (f >= 130)
        {
            s.Mouse = c; // keep the mouse away from screen edges so edge-scrolling doesn't interfere
        }

        if (f >= 130 && f < 165) s.Keys.Add(Key.Right);
        if (f >= 170 && f < 200) s.Keys.Add(Key.Up);
        if (f is 220 or 230) s.Scroll = 1f;
        if (f is 245) s.Scroll = -1f;
    }

    /// <summary>
    /// start, box-select one worker (default 1280x720 layout), then with Shift held: three right-clicks (f100-130), D + three depot
    /// placements on one worker (f150-200), Esc ends placement; idles so queued waypoints/ghosts can be seen.
    /// </summary>
    private static void Queue(int f, int w, int h, ScriptFrame s)
    {
        Start(f, w, h, s);
        var c = new Vec2(w / 2f, h / 2f);
        var boxA = c + new Vec2(-165, -108);
        var boxB = c + new Vec2(-125, -70);
        if (f >= 50 && f < 60) s.Mouse = boxA;
        else if (f >= 60 && f <= 80) { s.Mouse = boxA + (boxB - boxA) * ((f - 60) / 20f); s.MouseDown[(int)MouseButton.Left] = true; }
        else if (f > 80) s.Mouse = c;

        Vec2[] moves = { c + new Vec2(220, -60), c + new Vec2(260, 80), c + new Vec2(120, 160) };
        Vec2[] depots = { c + new Vec2(-40, -150), c + new Vec2(40, -150), c + new Vec2(-200, 40) };
        if (f >= 98 && f < 205) s.Keys.Add(Key.LeftShift);
        for (int i = 0; i < 3; i++)
        {
            int move = 100 + i * 10;
            if (f >= move && f < move + 8) { s.Mouse = moves[i]; if (f < move + 2) s.MouseDown[(int)MouseButton.Right] = true; }
            int place = 160 + i * 14;
            if (f >= place && f < place + 12) { s.Mouse = depots[i]; if (f < place + 2) s.MouseDown[(int)MouseButton.Left] = true; }
        }
        if (f is 150 or 151) s.Keys.Add(Key.D);
        if (f is 210 or 211) s.Keys.Add(Key.Escape);
    }
}
