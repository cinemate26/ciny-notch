namespace CinyNotch.Diagnostics;

/// <summary>Writes to data\debug.log only when CINY_DEBUG=1 is set.</summary>
internal static class Log
{
    public static readonly bool On = Environment.GetEnvironmentVariable("CINY_DEBUG") == "1";
    private static readonly string FilePath = Path.Combine(CinyNotch.Services.Paths.Data, "debug.log");
    private static string? _last;

    private static readonly object Gate = new();

    public static void Write(string line)
    {
        if (!On) return;
        lock (Gate)
        {
            if (line == _last) return;
            _last = line;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never break the island.
            }
        }
    }
}
