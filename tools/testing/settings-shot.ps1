param([string]$Exe, [string]$Out)
# Opens the island with Settings showing, captures only the Settings window, then closes everything.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W5 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
[void][W5]::SetProcessDPIAware()
Get-Process CinyNotch -ErrorAction SilentlyContinue | Stop-Process; Start-Sleep -Milliseconds 500
$env:CINY_OPEN_SETTINGS = '1'
$p = Start-Process -FilePath $Exe -PassThru
Remove-Item Env:CINY_OPEN_SETTINGS
Start-Sleep -Seconds 3
$h = [W5]::FindWindow([NullString]::Value, "Ciny Notch settings")
"settings window: $h"
if ($h -ne [IntPtr]::Zero) {
  $r = New-Object W5+RECT; [void][W5]::GetWindowRect($h, [ref]$r)
  $b = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [System.Drawing.Graphics]::FromImage($b); $hdc = $g.GetHdc()
  [void][W5]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
  $b.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}
Stop-Process -Id $p.Id
