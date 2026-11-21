param([string]$OutDir, [string]$Exe)
# Pins the island open, posts mouse clicks to its window (the real cursor never moves),
# and screenshots the top strip after each click.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class T {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
  public static IntPtr IslandWindow(uint pid) {
    for (IntPtr h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, 2)) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid) continue;
      var sb = new System.Text.StringBuilder(128); GetClassName(h, sb, 128);
      if (sb.ToString().StartsWith("Avalonia")) return h;
    }
    return IntPtr.Zero;
  }
  public static void Click(IntPtr h, int x, int y) {
    IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
    PostMessage(h, 0x0200, IntPtr.Zero, lp);          // WM_MOUSEMOVE
    PostMessage(h, 0x0201, (IntPtr)1, lp);            // WM_LBUTTONDOWN
    PostMessage(h, 0x0202, IntPtr.Zero, lp);          // WM_LBUTTONUP
  }
}
"@
[void][T]::SetProcessDPIAware()
$cx = [int]([System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width / 2)
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Shot($name) {
  $b = New-Object System.Drawing.Bitmap 700, 230
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($cx - 350, 0, 0, 0, $b.Size); $g.Dispose()
  $b.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}

$env:CINY_DEBUG = '1'; $env:CINY_PIN_OPEN = '1'
Remove-Item (Join-Path $PSScriptRoot "..\..\data\debug.log") -ErrorAction SilentlyContinue
$island = Start-Process -FilePath $Exe -PassThru
Remove-Item Env:CINY_PIN_OPEN, Env:CINY_DEBUG
Start-Sleep -Seconds 3
$h = [T]::IslandWindow([uint32]$island.Id)
"island window: $h"

Shot "0-home"
[T]::Click($h, 162, 26); Start-Sleep -Milliseconds 500; Shot "1-music"
[T]::Click($h, 230, 26); Start-Sleep -Milliseconds 500; Shot "2-clipboard"
[T]::Click($h, 104, 26); Start-Sleep -Milliseconds 500; Shot "3-home-again"

Stop-Process -Id $island.Id
Get-Content (Join-Path $PSScriptRoot "..\..\data\debug.log") | Select-String "tab " | ForEach-Object { $_.Line }
