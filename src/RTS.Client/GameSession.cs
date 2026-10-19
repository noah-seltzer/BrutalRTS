using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

public sealed class GameStats
{
    public int UnitsTrained, UnitsLost, EnemyUnitsKilled, StructuresBuilt;
}

public sealed class MessageLog
{
    public readonly record struct Msg(string Text, Color Color, float Time);
    public const float Life = 5f;
    public readonly List<Msg> Items = new();
    readonly Dictionary<string, float> _last = new();

    public void Add(string text, Color color, float now, float minInterval = 0f)
    {
        if (minInterval > 0 && _last.TryGetValue(text, out var t) && now - t < minInterval) return;
        _last[text] = now;
        Items.Add(new Msg(text, color, now));
        if (Items.Count > 8) Items.RemoveAt(0);
    }

    public void Update(float now) => Items.RemoveAll(m => now - m.Time > Life);
}

/// <summary>One running skirmish: owns the World, camera, selection and per-game UI state.</summary>
public sealed partial class GameSession
{
    public const int Me = 0;
    public const float StepDt = 1f / 30f;
    public const float TS = World.TileSize;
    static readonly float[] Speeds = { 0.5f, 1f, 1.5f, 2f, 3f };

    public World World { get; }
    public Difficulty Difficulty { get; }
    public int Seed { get; }
    public Camera Camera { get; } = new();
    public SelectionManager Selection { get; } = new();
    public Effects Effects { get; } = new();
    public MessageLog Messages { get; } = new();
    public GameStats Stats { get; } = new();

    public bool Paused;
    public bool ShowHelp;
    int _speedIdx = 1;
    public float Speed => Speeds[_speedIdx];
    public float RealTime { get; private set; }
    public bool RequestMenu { get; private set; }
    public long StepsRun { get; private set; }

    int? _eventWinner;
    public int? Winner => World.Winner ?? _eventWinner;
    public bool GameOver => World.IsGameOver || _eventWinner != null;

    float _acc;
    internal readonly Color[] TileColors;
    public Vec2 MouseScreen { get; private set; }
    public Vec2 MouseWorld { get; private set; }
    public Layout Layout { get; private set; } = new();

    public GameSession(int seed, Difficulty difficulty, Layout layout)
    {
        Seed = seed;
        Difficulty = difficulty;
        World = World.CreateSkirmish(seed, difficulty);
        Layout = layout;
        Camera.View = layout.PlayArea;
        Camera.MapW = World.Width * TS;
        Camera.MapH = World.Height * TS;

        TileColors = new Color[World.Width * World.Height];
        for (int y = 0; y < World.Height; y++)
            for (int x = 0; x < World.Width; x++)
            {
                var t = World.TerrainAt(x, y);
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                float v = 0.92f + (h % 1000) / 1000f * 0.16f;
                TileColors[y * World.Width + x] = Pal.Scale(Pal.TerrainColor(t), v);
            }

        var hq = World.Entities.FirstOrDefault(e => e.Owner == Me && e.Kind == EntityKind.HQ)
                 ?? World.Entities.FirstOrDefault(e => e.Owner == Me);
        Camera.CenterOn(hq?.Pos ?? new Vec2(Camera.MapW / 2, Camera.MapH / 2));
        Messages.Add($"Skirmish started ({difficulty}). Destroy the enemy base!", Pal.Gold, 0);
    }

    // ---------------------------------------------------------------- helpers

    public (int x, int y) TileOf(Vec2 w)
        => (Math.Clamp((int)MathF.Floor(w.X / TS), 0, World.Width - 1), Math.Clamp((int)MathF.Floor(w.Y / TS), 0, World.Height - 1));

    public byte VisAt(Vec2 w) { var (x, y) = TileOf(w); return World.Visibility(Me, x, y); }

    /// <summary>Fog rule for drawing / picking: own always, neutral once explored, enemy only while visible.</summary>
    public bool IsVisible(Entity e)
    {
        if (e.Owner == Me) return true;
        return e.Owner < 0 ? VisAt(e.Pos) >= 1 : World.IsVisibleTo(Me, e);
    }

    public bool HasTech(EntityKind? req)
        => req == null || World.Entities.Any(e => e.Owner == Me && e.Kind == req.Value && e.Alive && !e.UnderConstruction);

    public PlayerState MyPlayer => World.Players[Me];

    // ---------------------------------------------------------------- update

    public void Update(InputState input, float dt, Layout layout)
    {
        Layout = layout;
        dt = Math.Clamp(dt, 0f, 0.25f);
        RealTime += dt;
        MouseScreen = input.MousePos;

        HandleGlobalKeys(input);

        if (!GameOver && !Paused)
        {
            _acc += dt * Speed;
            int n = 0;
            while (_acc >= StepDt && n < 12)
            {
                World.Step(StepDt);
                StepsRun++;
                _acc -= StepDt;
                n++;
            }
            if (n == 12) _acc = 0;
        }
        ProcessEvents();
        Selection.Prune(World);
        if (Placing != null && !Selection.Own(World, Me).Any(e => e.Def.Builds.Contains(Placing.Value))) Placing = null;

        Camera.Update(input, dt, layout, allowMouse: true);
        MouseWorld = Camera.ScreenToWorld(input.MousePos);

        BuildCard();
        if (!GameOver)
        {
            HandleMouse(input);
            HandleHotkeys(input);
            BuildCard(); // selection/mode may have changed this frame; keep the drawn card current
            HoveredButton = Card.FirstOrDefault(b => layout.CardSlot(b.Slot).Contains(input.MousePos));
        }
        else
        {
            Dragging = false; Placing = null; AttackMovePending = false;
            if (input.Pressed(Key.Enter) || input.Pressed(Key.Escape)) RequestMenu = true;
        }

        Effects.Update(dt);
        Messages.Update(RealTime);
        Pings.RemoveAll(p => RealTime - p.Time > 3f);
    }

    void HandleGlobalKeys(InputState input)
    {
        if (input.Pressed(Key.F1)) ShowHelp = !ShowHelp;
        if (input.Pressed(Key.P) && !GameOver) Paused = !Paused;
        if (input.Pressed(Key.F2)) _speedIdx = Math.Max(0, _speedIdx - 1);
        if (input.Pressed(Key.F3)) _speedIdx = Math.Min(Speeds.Length - 1, _speedIdx + 1);
        if (input.Pressed(Key.F10)) RequestMenu = true;
    }

    // ---------------------------------------------------------------- events

    public readonly List<(Vec2 Pos, float Time)> Pings = new();
    Vec2? _lastAlertPos;
    float _lastAttackMsg = -100;

    void ProcessEvents()
    {
        foreach (var ev in World.Events)
        {
            switch (ev.Type)
            {
                case GameEvent.Shot:
                    break; // tracers come from World.Shots (has both endpoints)
                case GameEvent.Explosion:
                    if (VisAt(ev.Pos) >= 2) Effects.Explosion(ev.Pos);
                    break;
                case GameEvent.UnitTrained:
                    if (ev.Owner == Me) { Stats.UnitsTrained++; Messages.Add("Unit ready", Pal.MoveMarker, RealTime, 0.5f); }
                    break;
                case GameEvent.StructureComplete:
                    if (ev.Owner == Me) { Stats.StructuresBuilt++; Messages.Add("Construction complete", Pal.MoveMarker, RealTime, 0.5f); }
                    break;
                case GameEvent.UnderAttack:
                    if (ev.Owner == Me)
                    {
                        _lastAlertPos = ev.Pos;
                        if (RealTime - _lastAttackMsg > 6f)
                        {
                            _lastAttackMsg = RealTime;
                            Messages.Add("Under attack! (Space: jump there)", Pal.AttackMarker, RealTime);
                            Pings.Add((ev.Pos, RealTime));
                            Effects.Ping(ev.Pos, Pal.AttackMarker);
                        }
                    }
                    break;
                case GameEvent.UnitLost:
                    if (ev.Owner == Me) { Stats.UnitsLost++; Messages.Add("Unit lost", new Color(230, 140, 90), RealTime, 3f); }
                    else if (ev.Owner != Me && ev.Owner >= 0) Stats.EnemyUnitsKilled++;
                    break;
                case GameEvent.InsufficientCredits:
                    if (ev.Owner == Me) Messages.Add("Not enough credits", Pal.Gold, RealTime, 1f);
                    break;
                case GameEvent.PopCapped:
                    if (ev.Owner == Me) Messages.Add("Supply limit reached - build a Supply Depot", Pal.Gold, RealTime, 2f);
                    break;
                case GameEvent.Victory:
                    _eventWinner ??= ev.Owner >= 0 ? ev.Owner : Me;
                    break;
                case GameEvent.Defeat:
                    _eventWinner ??= ev.Owner == Me ? 1 : Me;
                    break;
            }
        }
        World.Events.Clear();

        foreach (var shot in World.Shots)
        {
            if (VisAt(shot.From) < 2 && VisAt(shot.To) < 2) continue;
            var col = shot.Owner == Me ? new Color(150, 210, 255) : new Color(255, 170, 120);
            if (shot.Kind == EntityKind.Tank || shot.Kind == EntityKind.Turret) col = Pal.Lerp(col, new Color(255, 240, 160), 0.5f);
            Effects.Shot(shot.From, shot.To, col);
        }
        World.Shots.Clear();
    }
}
