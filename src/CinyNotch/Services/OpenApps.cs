using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CinyNotch.Diagnostics;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace CinyNotch.Services;

internal sealed record OpenApp(string Key, string Name, nint Window, AvBitmap? Icon, bool Active);

/// <summary>
/// The apps that are open right now, one entry per program, using the same rules the
/// Windows taskbar uses to decide which windows get a button. Read-only, on this PC.
/// </summary>
internal static class OpenApps
{
    private static readonly Dictionary<string, (string Name, AvBitmap? Icon)> Known = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, long> FirstSeen = new(StringComparer.OrdinalIgnoreCase);
    private static long _order;

    /// <summary>Open apps in a stable order (first seen first), the foreground one marked.</summary>
    public static List<OpenApp> List(int ownPid, int max = 14)
    {
        var foreground = GetForegroundWindow();
        var byApp = new Dictionary<string, OpenApp>(StringComparer.OrdinalIgnoreCase);

        EnumWindows((h, _) =>
        {
            try
            {
                if (!ShowsOnTaskbar(h)) return true;
                var pid = PidOf(h);
                if (pid == ownPid) return true;
                var path = ExePath(pid);
                if (path is null) return true;

                // Store apps run inside a frame host; the real app is a child window.
                if (Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var inner = HostedAppPath(h);
                    if (inner is null) return true;
                    path = inner;
                }

                var isActive = h == foreground;
                if (byApp.TryGetValue(path, out var existing))
                {
                    if (isActive && !existing.Active) byApp[path] = existing with { Active = true, Window = h };
                    return true; // windows come top-most first: keep the most recent one
                }
                var (name, icon) = Describe(path, h);
                byApp[path] = new OpenApp(path, name, h, icon, isActive);
                if (!FirstSeen.ContainsKey(path)) FirstSeen[path] = _order++;
            }
            catch
            {
                // A window that closes mid-look is simply skipped.
            }
            return true;
        }, 0);

        return byApp.Values.OrderBy(a => FirstSeen.GetValueOrDefault(a.Key)).Take(max).ToList();
    }

    /// <summary>Brings the app's window to the front, restoring it if minimised.</summary>
    public static void Activate(nint window)
    {
        if (IsIconic(window)) ShowWindow(window, 9); // SW_RESTORE
        if (!SetForegroundWindow(window)) SwitchToThisWindow(window, true);
    }

    // Raymond Chen's rule: visible, not cloaked, and either marked as an app window or an
    // unowned window that isn't a tool window.
    private static bool ShowsOnTaskbar(nint h)
    {
        if (!IsWindowVisible(h)) return false;
        if (IsCloaked(h)) return false;
        var ex = (long)GetWindowLongPtr(h, -20);
        const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
        if ((ex & WS_EX_APPWINDOW) != 0) return true;
        if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0) return false;
        if (GetWindow(h, 4) != 0) return false; // GW_OWNER
        return GetWindowTextLength(h) > 0;
    }

    private static bool IsCloaked(nint h) =>
        DwmGetWindowAttribute(h, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static (string Name, AvBitmap? Icon) Describe(string path, nint window)
    {
        if (Known.TryGetValue(path, out var k) && k.Icon is not null) return k;

        string name;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription! : Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            name = Path.GetFileNameWithoutExtension(path);
        }

        var icon = IconFromExe(path) ?? IconFromWindow(window);
        Known[path] = (name, icon);
        return (name, icon);
    }

    private static AvBitmap? IconFromExe(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractIcon(path, 0, 64);
            return icon is null ? null : ToAvalonia(icon);
        }
        catch
        {
            return null; // Store app folders can refuse access; the window's own icon is next.
        }
    }

    private static AvBitmap? IconFromWindow(nint window)
    {
        try
        {
            // WM_GETICON big, then the class icon. These handles belong to the window: not destroyed here.
            var h = SendMessageTimeout(window, 0x007F, 1, 0, 0x2, 200, out var result) != 0 ? result : 0;
            if (h == 0) h = GetClassLongPtr(window, -14); // GCLP_HICON
            if (h == 0) return null;
            using var icon = System.Drawing.Icon.FromHandle(h);
            return ToAvalonia(icon);
        }
        catch
        {
            return null;
        }
    }

    private static AvBitmap ToAvalonia(System.Drawing.Icon icon)
    {
        using var bmp = icon.ToBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        return new AvBitmap(ms);
    }

    private static uint PidOf(nint h)
    {
        GetWindowThreadProcessId(h, out var pid);
        return pid;
    }

    private static string? ExePath(uint pid)
    {
        var p = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (p == 0) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(p, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(p);
        }
    }

    private static string? HostedAppPath(nint frame)
    {
        string? found = null;
        var framePid = PidOf(frame);
        EnumChildWindows(frame, (child, _) =>
        {
            var pid = PidOf(child);
            if (pid != framePid)
            {
                found = ExePath(pid);
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private delegate bool EnumProc(nint hwnd, nint data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumProc proc, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(nint h, bool altTab);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint h, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint h, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint h, uint msg, nint w, nint l, uint flags, uint timeout, out nint result);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint h, int attr, out int value, int size);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref int size);
}
