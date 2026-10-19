namespace RTS.Game.Platform;

/// <summary>Tiny logger: console + rts-game.log next to the executable.</summary>
public static class Log
{
    private static readonly object s_lock = new();
    private static StreamWriter? s_file;

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "rts-game.log");

    public static void Open()
    {
        try { s_file = new StreamWriter(FilePath, append: false) { AutoFlush = true }; }
        catch { s_file = null; }
    }

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level} {msg}";
        lock (s_lock)
        {
            Console.WriteLine(line);
            s_file?.WriteLine(line);
        }
    }

    public static void Close()
    {
        lock (s_lock) { s_file?.Dispose(); s_file = null; }
    }
}
