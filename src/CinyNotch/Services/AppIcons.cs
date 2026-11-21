using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using CinyNotch.Diagnostics;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;
using RenderTargetBitmap = Avalonia.Media.Imaging.RenderTargetBitmap;

namespace CinyNotch.Services;

/// <summary>
/// Logos for the apps the island talks about. Wherever possible they come from what is
/// already installed on this PC (the Claude Code extension's logo, VS Code's tile, an
/// app's own .exe icon), read at runtime and never copied into the app. YouTube,
/// YouTube Music and Spotify-in-a-browser have nothing installed, so simple marks are drawn.
/// </summary>
internal static class AppIcons
{
    private static readonly Dictionary<string, AvBitmap?> Cache = new();

    public static AvBitmap? Claude => Get("claude", () => LoadFile(FindClaudeLogo()));
    public static AvBitmap? VsCode => Get("vscode", () => LoadFile(FindVsCodeTile()) ?? ForProcess("Code"));
    public static AvBitmap YouTube => Get("youtube", DrawYouTube)!;
    public static AvBitmap YouTubeMusic => Get("ytmusic", DrawYouTubeMusic)!;
    public static AvBitmap SpotifyMark => Get("spotify-mark", DrawSpotify)!;

    /// <summary>The icon of a running program, by process name (for example "chrome").</summary>
    public static AvBitmap? ForProcess(string processName) => Get("proc:" + processName.ToLowerInvariant(), () =>
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                using (p)
                {
                    var path = p.MainModule?.FileName;
                    if (path is not null && FromExe(path) is { } icon) return icon;
                }
            }
        }
        catch
        {
            // Some processes (Store apps, elevated ones) don't let us read their path.
        }
        return null;
    });

    private static AvBitmap? Get(string key, Func<AvBitmap?> make)
    {
        if (Cache.TryGetValue(key, out var hit)) return hit;
        AvBitmap? made = null;
        try { made = make(); } catch (Exception ex) { Log.Write($"icon {key}: {ex.Message}"); }
        // Missing program icons are retried later (the app may start); found ones are kept.
        if (made is not null || !key.StartsWith("proc:")) Cache[key] = made;
        return made;
    }

    private static AvBitmap? LoadFile(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        using var s = File.OpenRead(path);
        return AvBitmap.DecodeToWidth(s, 64);
    }

    private static AvBitmap? FromExe(string path)
    {
        using var icon = System.Drawing.Icon.ExtractIcon(path, 0, 64);
        if (icon is null) return null;
        using var bmp = icon.ToBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        return new AvBitmap(ms);
    }

    private static string? FindClaudeLogo()
    {
        var ext = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
        if (!Directory.Exists(ext)) return null;
        var dir = new DirectoryInfo(ext).GetDirectories("anthropic.claude-code-*").OrderByDescending(d => d.LastWriteTimeUtc).FirstOrDefault();
        return dir is null ? null : Path.Combine(dir.FullName, "resources", "claude-logo.png");
    }

    private static string? FindVsCodeTile()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code");
        if (!Directory.Exists(root)) return null;
        // Newer VS Code keeps each version in its own folder; older ones keep "resources" at the top.
        var candidates = new[] { root }.Concat(Directory.GetDirectories(root))
            .Select(d => Path.Combine(d, "resources", "app", "resources", "win32", "code_150x150.png"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc);
        return candidates.FirstOrDefault();
    }

    // ---------- drawn marks ----------

    private static AvBitmap Draw(Action<DrawingContext> paint)
    {
        var bmp = new RenderTargetBitmap(new PixelSize(64, 64));
        using (var ctx = bmp.CreateDrawingContext()) paint(ctx);
        return bmp;
    }

    private static AvBitmap DrawYouTube() => Draw(ctx =>
    {
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#FF0033")), null, new RoundedRect(new Rect(4, 13, 56, 38), 11));
        ctx.DrawGeometry(Brushes.White, null, Geometry.Parse("M 26,23 L 26,41 L 41,32 Z"));
    });

    private static AvBitmap DrawYouTubeMusic() => Draw(ctx =>
    {
        ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#FF0033")), null, new Point(32, 32), 28, 28);
        ctx.DrawEllipse(null, new Pen(Brushes.White, 3), new Point(32, 32), 15, 15);
        ctx.DrawGeometry(Brushes.White, null, Geometry.Parse("M 28,25 L 28,39 L 39,32 Z"));
    });

    private static AvBitmap DrawSpotify() => Draw(ctx =>
    {
        ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#1ED760")), null, new Point(32, 32), 28, 28);
        var pen = new Pen(Brushes.Black, 5.5, lineCap: PenLineCap.Round);
        ctx.DrawGeometry(null, pen, Geometry.Parse("M 15,24 Q 33,17 50,27"));
        ctx.DrawGeometry(null, new Pen(Brushes.Black, 4.5, lineCap: PenLineCap.Round), Geometry.Parse("M 17,34 Q 32,28 46,36"));
        ctx.DrawGeometry(null, new Pen(Brushes.Black, 3.5, lineCap: PenLineCap.Round), Geometry.Parse("M 19,43 Q 31,38 43,44"));
    });

    // ---------- window titles, to tell which site a browser is playing ----------

    /// <summary>Titles of the visible top-level windows of the given processes.</summary>
    public static List<string> WindowTitles(string processName)
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(processName)) { pids.Add((uint)p.Id); p.Dispose(); }
        var titles = new List<string>();
        if (pids.Count == 0) return titles;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid)) return true;
            var sb = new StringBuilder(512);
            if (GetWindowText(h, sb, sb.Capacity) > 0) titles.Add(sb.ToString());
            return true;
        }, 0);
        return titles;
    }

    private delegate bool EnumProc(nint hwnd, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, nint data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
}
