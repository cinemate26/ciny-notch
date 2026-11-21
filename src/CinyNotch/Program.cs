using Avalonia;

namespace CinyNotch;

internal static class Program
{
    /// <summary>Signals a running copy listens for (see App.ListenForSignals).</summary>
    public const string QuitSignal = "CinyNotch.Quit";
    public const string ShowSignal = "CinyNotch.Show";

    [STAThread]
    public static int Main(string[] args)
    {
        // "CinyNotch.exe --quit" (the "Close Ciny Notch" shortcut) closes the running island.
        var quit = args.Any(a => a.Equals("--quit", StringComparison.OrdinalIgnoreCase)
                              || a.Equals("--close", StringComparison.OrdinalIgnoreCase)
                              || a.Equals("/quit", StringComparison.OrdinalIgnoreCase));

        // One island per PC. A second launch tells the running one what to do, then exits.
        using var mutex = new Mutex(true, "CinyNotch.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            Signal(quit ? QuitSignal : ShowSignal);
            return 0;
        }
        if (quit) return 0; // nothing running to close

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void Signal(string name)
    {
        if (EventWaitHandle.TryOpenExisting(name, out var handle))
        {
            handle.Set();
            handle.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
