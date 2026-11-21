using Avalonia.Threading;
using Microsoft.Win32;

namespace CinyNotch.Services;

/// <summary>
/// Knows when an app is using the microphone or camera (a call, usually), by reading the
/// same privacy records Windows uses for its own mic and camera indicators. Read-only.
/// </summary>
internal sealed class MicUseWatcher
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private readonly DispatcherTimer _timer;

    public bool MicInUse { get; private set; }
    public bool CameraInUse { get; private set; }
    public string App { get; private set; } = "";
    public DateTime Since { get; private set; }

    public event Action? Changed;

    public MicUseWatcher()
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Poll());
    }

    public void Start()
    {
        Poll();
        _timer.Start();
    }

    private int _busy;

    /// <summary>Reads the privacy records off the UI thread (there can be hundreds), then reports back.</summary>
    private void Poll()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                var (mic, app) = InUse("microphone");
                var (cam, _) = InUse("webcam");
                Dispatcher.UIThread.Post(() => Apply(mic, cam, app));
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private void Apply(bool mic, bool cam, string app)
    {
        if (mic == MicInUse && cam == CameraInUse && app == App) return;

        if (mic && !MicInUse) Since = DateTime.Now;
        MicInUse = mic;
        CameraInUse = cam;
        App = app;
        Changed?.Invoke();
    }

    private static (bool InUse, string App) InUse(string capability)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\{capability}");
            if (key is null) return (false, "");
            foreach (var (name, sub) in Entries(key))
            {
                if (IsActive(sub)) return (true, FriendlyName(name));
            }
        }
        catch
        {
            // No access to the records: report nothing in use.
        }
        return (false, "");
    }

    private static IEnumerable<(string Name, RegistryKey Key)> Entries(RegistryKey root)
    {
        foreach (var name in root.GetSubKeyNames())
        {
            using var sub = root.OpenSubKey(name);
            if (sub is null) continue;
            if (name == "NonPackaged")
            {
                foreach (var inner in sub.GetSubKeyNames())
                {
                    using var app = sub.OpenSubKey(inner);
                    if (app is not null) yield return (inner, app);
                }
            }
            else
            {
                yield return (name, sub);
            }
        }
    }

    private static bool IsActive(RegistryKey key)
    {
        var start = key.GetValue("LastUsedTimeStart") is long s ? s : 0;
        var stop = key.GetValue("LastUsedTimeStop") is long e ? e : -1;
        return start > 0 && stop == 0;
    }

    private static string FriendlyName(string keyName)
    {
        // Packaged: "MSTeams_8wekyb3d8bbwe". Desktop: "C:#Program Files#...#Zoom.exe".
        var name = keyName.Contains('#') ? Path.GetFileNameWithoutExtension(keyName.Replace('#', '\\')) : keyName.Split('_')[0];
        return name switch
        {
            var n when n.Contains("Teams", StringComparison.OrdinalIgnoreCase) => "Teams",
            var n when n.Contains("Zoom", StringComparison.OrdinalIgnoreCase) => "Zoom",
            var n when n.Contains("Discord", StringComparison.OrdinalIgnoreCase) => "Discord",
            var n when n.Contains("chrome", StringComparison.OrdinalIgnoreCase) => "Chrome",
            var n when n.Contains("msedge", StringComparison.OrdinalIgnoreCase) => "Edge",
            var n when n.Contains("firefox", StringComparison.OrdinalIgnoreCase) => "Firefox",
            var n when n.Contains("Slack", StringComparison.OrdinalIgnoreCase) => "Slack",
            _ => name,
        };
    }
}
