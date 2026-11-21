using System.Management;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CinyNotch.Diagnostics;

namespace CinyNotch.Services;

/// <summary>
/// Screen brightness. Laptop screens go through Windows (WMI); external monitors through
/// DDC/CI, the standard monitors use for brightness over the display cable. Monitors with
/// DDC/CI switched off in their own menu can't be changed from the PC.
/// </summary>
internal sealed class BrightnessService : IDisposable
{
    private ManagementEventWatcher? _watcher;
    private bool _useWmi;
    private bool _useDdc;

    public bool Supported => _useWmi || _useDdc;
    public int Level { get; private set; }

    /// <summary>Brightness changed from outside the island (keys, Settings).</summary>
    public event Action<int>? ChangedOutside;
    /// <summary>The first reading arrived, or the level changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        Task.Run(() =>
        {
            int? level = ReadWmi();
            if (level is not null) _useWmi = true;
            else
            {
                level = ReadDdc();
                if (level is not null) _useDdc = true;
            }
            Dispatcher.UIThread.Post(() =>
            {
                Level = level ?? 0;
                Log.Write($"brightness: {(_useWmi ? "laptop screen" : _useDdc ? "monitor (DDC/CI)" : "not adjustable")} {Level}%");
                Changed?.Invoke();
            });
            if (_useWmi) WatchWmi();
        });
    }

    public void Set(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        Level = percent;
        Task.Run(() =>
        {
            if (_useWmi) SetWmi(percent);
            else if (_useDdc) SetDdc(percent);
        });
    }

    private static int? ReadWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var o in searcher.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch
        {
            // Not a laptop screen.
        }
        return null;
    }

    private static void SetWmi(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in searcher.Get()) o.InvokeMethod("WmiSetBrightness", [1u, (byte)percent]);
        }
        catch (Exception ex)
        {
            Log.Write($"brightness (wmi): {ex.Message}");
        }
    }

    private void WatchWmi()
    {
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope("root\\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                var value = Convert.ToInt32(e.NewEvent["Brightness"]);
                Dispatcher.UIThread.Post(() =>
                {
                    if (value == Level) return;
                    Level = value;
                    ChangedOutside?.Invoke(value);
                    Changed?.Invoke();
                });
            };
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Write($"brightness watch: {ex.Message}");
        }
    }

    // ---------- DDC/CI for external monitors ----------

    private static int? ReadDdc()
    {
        int? result = null;
        ForEachPhysicalMonitor(h =>
        {
            if (result is null && GetMonitorBrightness(h, out var min, out var cur, out var max) && max > min)
                result = (int)Math.Round((cur - min) * 100.0 / (max - min));
        });
        return result;
    }

    private static void SetDdc(int percent)
    {
        ForEachPhysicalMonitor(h =>
        {
            if (GetMonitorBrightness(h, out var min, out _, out var max) && max > min)
                SetMonitorBrightness(h, (uint)(min + (max - min) * percent / 100.0));
        });
    }

    private static void ForEachPhysicalMonitor(Action<nint> action)
    {
        var monitors = new List<nint>();
        EnumDisplayMonitors(0, 0, (h, _, _, _) => { monitors.Add(h); return true; }, 0);
        foreach (var hMon in monitors)
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out var count) || count == 0) continue;
            var array = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, array)) continue;
            try
            {
                foreach (var pm in array) action(pm.hPhysicalMonitor);
            }
            finally
            {
                DestroyPhysicalMonitors(count, array);
            }
        }
    }

    public void Dispose()
    {
        try { _watcher?.Stop(); _watcher?.Dispose(); } catch { }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public nint hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, nint rect, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);

    [DllImport("dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint hMonitor, out uint count);

    [DllImport("dxva2.dll")]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(nint hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll")]
    private static extern bool GetMonitorBrightness(nint monitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll")]
    private static extern bool SetMonitorBrightness(nint monitor, uint value);
}
