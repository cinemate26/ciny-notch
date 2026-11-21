param([string]$Exe, [string]$OutDir, [string[]]$Tabs, [int]$Wait = 3)
# For each tab: start the island pinned open on that tab, screenshot the top strip, stop it.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class S2 { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[void][S2]::SetProcessDPIAware()
$cx = [int]([System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width / 2)
New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($tab in $Tabs) {
  Get-Process CinyNotch -ErrorAction SilentlyContinue | Stop-Process
  Start-Sleep -Milliseconds 500
  $env:CINY_DEBUG = '1'; $env:CINY_PIN_OPEN = $tab
  $p = Start-Process -FilePath $Exe -PassThru
  Remove-Item Env:CINY_PIN_OPEN, Env:CINY_DEBUG
  Start-Sleep -Seconds $Wait
  $b = New-Object System.Drawing.Bitmap 720, 360
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($cx - 360, 0, 0, 0, $b.Size); $g.Dispose()
  $b.Save((Join-Path $OutDir "$tab.png"), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
  "$tab done (pid $($p.Id))"
}
