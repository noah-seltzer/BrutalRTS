// RTS.Game entry point (platform layer).
//   dotnet run --project src/RTS.Game                 -> runs RTS.Client.GameClient
//   dotnet run --project src/RTS.Game -- --demo       -> runs the built-in DemoApp (platform test)
// Extra flags: --nosplash, --novsync, --autoquit <seconds> (smoke test: exit after N seconds),
//              --screenshot <file.bmp> (saves splash + final frame; use with --autoquit).
// If built with -p:NoClient=true (or RTS.Client is missing), NO_CLIENT is defined and DemoApp is always used.
using System.Globalization;
using RTS.Core;
using RTS.Game;
using RTS.Game.Platform;

Log.Open();
int exitCode = 0;
try
{
    bool demo = args.Contains("--demo");
    double.TryParse(Arg("--autoquit"), CultureInfo.InvariantCulture, out double autoQuit);

    IGameApp app;
#if NO_CLIENT
    if (!demo) Log.Info("Built without RTS.Client (NO_CLIENT) - running DemoApp");
    app = new DemoApp();
#else
    app = demo ? new DemoApp() : new RTS.Client.GameClient();
#endif
    Log.Info($"Starting '{app.Title}' ({app.GetType().FullName})");

    using var host = new GameHost(app, new GameHostOptions
    {
        ShowSplash = !args.Contains("--nosplash"),
        VSync = !args.Contains("--novsync"),
        AutoQuitSeconds = autoQuit,
        ScreenshotPath = Arg("--screenshot"),
    });
    host.Run();
}
catch (Exception ex)
{
    Log.Error(ex.ToString());
    exitCode = 1;
}
finally
{
    Log.Info($"Exit code {exitCode}");
    Log.Close();
}
return exitCode;

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
