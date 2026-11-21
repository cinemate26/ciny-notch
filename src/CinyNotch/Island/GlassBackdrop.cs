using System.Numerics;
using System.Runtime.InteropServices;
using CinyNotch.Diagnostics;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace CinyNotch.Island;

/// <summary>
/// Real frosted glass behind the island.
///
/// Windows only blurs whole windows, and Avalonia's own blur covers its full window
/// rectangle. So the glass lives in a second window, the same size as the island
/// window and sitting directly beneath it. That window holds a single composition
/// visual painted with the system's blurred "host backdrop", clipped to the island's
/// rounded shape and moved every frame with the island's spring. It never takes input.
/// </summary>
internal sealed unsafe class GlassBackdrop : IDisposable
{
    private const string ClassName = "CinyNotch.Glass";

    private readonly WndProc _wndProc = DefWindowProc; // kept alive for the class
    private nint _hwnd;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private SpriteVisual? _glass;
    private CompositionRoundedRectangleGeometry? _shape;
    private bool _visible = true;

    public bool IsReady => _glass is not null;

    public static GlassBackdrop? TryCreate(int x, int y, int width, int height)
    {
        var glass = new GlassBackdrop();
        try
        {
            glass.Create(x, y, width, height);
            return glass;
        }
        catch (Exception ex)
        {
            Log.Write($"glass unavailable: {ex.Message}");
            glass.Dispose();
            return null;
        }
    }

    private void Create(int x, int y, int width, int height)
    {
        EnsureDispatcherQueue();

        var hInstance = GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        RegisterClassEx(ref wc); // a second registration fails harmlessly

        const uint exStyle = WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                             | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT;
        _hwnd = CreateWindowEx(exStyle, ClassName, "", WS_POPUP, x, y, width, height, 0, 0, hInstance, 0);
        if (_hwnd == 0) throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()})");
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);

        // Windows 11 draws the host backdrop brush as plain black unless the window opts in.
        var useBackdrop = 1;
        var hr = DwmSetWindowAttribute(_hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref useBackdrop, sizeof(int));
        Log.Write($"host backdrop opt-in hr=0x{hr:X8}");

        _compositor = new Compositor();
        _target = CreateTarget(_compositor, _hwnd);

        var root = _compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = Vector2.One;
        _target.Root = root;

        _shape = _compositor.CreateRoundedRectangleGeometry();
        _glass = _compositor.CreateSpriteVisual();
        _glass.Brush = _compositor.CreateHostBackdropBrush();
        _glass.Clip = _compositor.CreateGeometricClip(_shape);
        root.Children.InsertAtTop(_glass);

        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        Log.Write("glass ready");
    }

    /// <summary>Moves the glass to the island's current shape (pixels, relative to the window).</summary>
    public void Update(double x, double y, double width, double height, double radius, bool flatTop)
    {
        if (_glass is null || _shape is null) return;

        _glass.Offset = new Vector3((float)x, (float)y, 0);
        _glass.Size = new Vector2((float)width, (float)height);

        // A notch has a flat top: the rounded shape starts above the visual, so only
        // the bottom corners show.
        var lift = flatTop ? (float)radius : 0f;
        _shape.Offset = new Vector2(0, -lift);
        _shape.Size = new Vector2((float)width, (float)height + lift);
        _shape.CornerRadius = new Vector2((float)radius, (float)radius);
    }

    public void MoveTo(int x, int y, int width, int height) =>
        SetWindowPos(_hwnd, 0, x, y, width, height, SWP_NOACTIVATE | SWP_NOZORDER);

    /// <summary>Keeps the glass directly beneath the island window.</summary>
    public void StackBelow(nint island) =>
        SetWindowPos(_hwnd, island, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public void SetVisible(bool visible)
    {
        if (_hwnd == 0 || visible == _visible) return;
        _visible = visible;
        ShowWindow(_hwnd, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
    }

    public void Dispose()
    {
        _glass?.Dispose();
        _shape?.Dispose();
        _target?.Dispose();
        _compositor?.Dispose();
        if (_hwnd != 0) DestroyWindow(_hwnd);
        _hwnd = 0;
    }

    // ---------- composition plumbing ----------

    private static void EnsureDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,     // DQTYPE_THREAD_CURRENT
            apartmentType = 0,  // DQTAT_COM_NONE
        };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _));
    }

    private static DesktopWindowTarget CreateTarget(Compositor compositor, nint hwnd)
    {
        var iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"); // ICompositorDesktopInterop
        var unknown = ((IWinRTObject)compositor).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var interop));
        try
        {
            // Slot 3: CreateDesktopWindowTarget(HWND, BOOL isTopmost, IDesktopWindowTarget**)
            var vtable = *(nint**)interop;
            var create = (delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)vtable[3];
            nint targetPtr;
            Marshal.ThrowExceptionForHR(create(interop, hwnd, 0, &targetPtr));
            var target = DesktopWindowTarget.FromAbi(targetPtr);
            Marshal.Release(targetPtr);
            return target;
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    // ---------- Win32 ----------

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;
    private const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize, threadType, apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    private const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
