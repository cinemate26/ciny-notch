using System.Runtime.InteropServices;
using CinyNotch.Diagnostics;

namespace CinyNotch.Native;

/// <summary>
/// A hidden, message-only window. It receives the global keyboard shortcuts and the
/// "clipboard changed" notice, neither of which needs anything on screen.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private const string ClassName = "CinyNotch.Messages";
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly nint HWND_MESSAGE = -3;

    private readonly WndProc _proc;
    private readonly List<int> _hotkeys = new();
    private nint _hwnd;

    public event Action<int>? HotKey;
    public event Action? ClipboardChanged;

    public MessageWindow()
    {
        _proc = Proc;
        var hInstance = GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        RegisterClassEx(ref wc);
        _hwnd = CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, hInstance, 0);
        if (_hwnd == 0) Log.Write("message window failed");
    }

    [Flags]
    public enum Mod : uint { Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8, NoRepeat = 0x4000 }

    /// <summary>Registers a global shortcut. Returns false if another app already owns it.</summary>
    public bool RegisterHotKey(int id, Mod mods, uint key)
    {
        var ok = _hwnd != 0 && RegisterHotKeyNative(_hwnd, id, (uint)(mods | Mod.NoRepeat), key);
        if (ok) _hotkeys.Add(id);
        else Log.Write($"hotkey {id} unavailable");
        return ok;
    }

    public void ListenToClipboard()
    {
        if (_hwnd != 0 && !AddClipboardFormatListener(_hwnd)) Log.Write("clipboard listener failed");
    }

    private nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_HOTKEY) HotKey?.Invoke((int)wParam);
        else if (msg == WM_CLIPBOARDUPDATE) ClipboardChanged?.Invoke();
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd == 0) return;
        foreach (var id in _hotkeys) UnregisterHotKey(_hwnd, id);
        RemoveClipboardFormatListener(_hwnd);
        DestroyWindow(_hwnd);
        _hwnd = 0;
    }

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "RegisterHotKey")]
    private static extern bool RegisterHotKeyNative(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll")]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
