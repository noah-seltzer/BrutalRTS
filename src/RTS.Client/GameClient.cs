using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

/// <summary>Top-level app: main menu <-> skirmish session. Driven by the platform layer.</summary>
public sealed class GameClient : IGameApp
{
    public enum ScreenKind { Menu, Game }

    public string Title => "BRUTAL RTS";
    public bool WantsQuit { get; private set; }
    public ScreenKind Screen { get; private set; } = ScreenKind.Menu;
    public MainMenu Menu { get; } = new();
    public GameSession? Session { get; private set; }

    /// <summary>Set for deterministic games (tests); otherwise a random seed per game.</summary>
    public int? FixedSeed { get; set; }

    readonly Layout _layout = new();
    public Layout Layout => _layout;

    public GameClient() { }

    public void StartGame(Difficulty difficulty)
    {
        int seed = FixedSeed ?? Random.Shared.Next(1, int.MaxValue);
        Session = new GameSession(seed, difficulty, _layout);
        Screen = ScreenKind.Game;
    }

    public void Update(InputState input, float dt)
    {
        if (Screen == ScreenKind.Menu)
        {
            switch (Menu.Update(input, dt, _layout))
            {
                case MenuAction.Start: StartGame(Menu.Selected); break;
                case MenuAction.Quit: WantsQuit = true; break;
            }
            return;
        }

        Session!.Update(input, dt, _layout);
        if (Session.RequestMenu)
        {
            Session = null;
            Screen = ScreenKind.Menu;
        }
    }

    public void Draw(IRenderer2D r)
    {
        _layout.W = Math.Max(320, r.ScreenWidth);
        _layout.H = Math.Max(400, r.ScreenHeight);
        if (Screen == ScreenKind.Menu || Session == null)
        {
            Menu.Draw(r, _layout);
            return;
        }
        r.Clear(Color.Black);
        Session.Camera.View = _layout.PlayArea;
        Session.Camera.Clamp();
        WorldRenderer.Draw(r, Session);
        Hud.Draw(r, Session);
    }
}
