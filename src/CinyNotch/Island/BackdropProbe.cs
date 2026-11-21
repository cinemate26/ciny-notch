using System.Runtime.InteropServices;

namespace CinyNotch.Island;

/// <summary>
/// How bright the screen is around the island, from 0 (black) to 1 (white). It looks at thin
/// strips just outside the island's edges, never under it, so the island never measures
/// itself. Each strip is shrunk to a few pixels by the graphics card, so this costs almost nothing.
/// </summary>
internal static class BackdropProbe
{
    private const int SampleW = 8, SampleH = 4;

    /// <summary>Average brightness of the given screen rectangles (physical pixels).</summary>
    public static double Brightness(IEnumerable<(int X, int Y, int W, int H)> rects)
    {
        var screen = GetDC(0);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, SampleW, SampleH);
        var old = SelectObject(mem, bmp);
        SetStretchBltMode(mem, 4); // HALFTONE: a proper average, not a sample of single pixels

        double total = 0;
        var count = 0;
        var pixels = new int[SampleW * SampleH];
        var info = new BITMAPINFO
        {
            biSize = Marshal.SizeOf<BITMAPINFO>(), biWidth = SampleW, biHeight = -SampleH,
            biPlanes = 1, biBitCount = 32, biCompression = 0,
        };

        try
        {
            foreach (var (x, y, w, h) in rects)
            {
                if (w <= 0 || h <= 0) continue;
                if (!StretchBlt(mem, 0, 0, SampleW, SampleH, screen, x, y, w, h, 0x00CC0020)) continue; // SRCCOPY
                if (GetDIBits(mem, bmp, 0, SampleH, pixels, ref info, 0) == 0) continue;
                foreach (var p in pixels)
                {
                    var r = (p >> 16) & 0xFF;
                    var g = (p >> 8) & 0xFF;
                    var b = p & 0xFF;
                    total += (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                    count++;
                }
            }
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(0, screen);
        }
        return count == 0 ? 0.2 : total / count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint hdc, int mode);
    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(nint dst, int dx, int dy, int dw, int dh, nint src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint hdc, nint bmp, uint start, uint lines, [Out] int[] bits, ref BITMAPINFO info, uint usage);
}
