param([string]$Out, [string]$Step = "shot", [int]$Pid0 = 0)
# Probe helper for the island: screenshot of the top-centre strip only, cursor moves, hit tests.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class P {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
}
"@
[void][P]::SetProcessDPIAware()
$sw = [P]::GetSystemMetrics(0)
$cx = [int]($sw / 2)

function Shot($path) {
  $w = 1000; $h = 460
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($cx - $w / 2, 0, 0, 0, $bmp.Size)
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

function HitOwner($x, $y) {
  $p = New-Object P+POINT; $p.X = $x; $p.Y = $y
  $h = [P]::WindowFromPoint($p)
  $procId = 0; [void][P]::GetWindowThreadProcessId($h, [ref]$procId)
  return $procId
}

switch ($Step) {
  "shot" { Shot $Out; "screen width px: $sw" }
  "hover" {
    $orig = New-Object P+POINT; [void][P]::GetCursorPos([ref]$orig)
    "pill hit before hover -> owner pid $(HitOwner $cx 12) (island pid $Pid0)"
    $moved = 0
    for ($i = 0; $i -lt 18; $i++) {
      $c = New-Object P+POINT; [void][P]::GetCursorPos([ref]$c)
      if ($i -gt 0 -and ($c.X -ne $cx -or $c.Y -ne 12)) { $moved++ }
      [void][P]::SetCursorPos($cx, 12)
      Start-Sleep -Milliseconds 50
    }
    "cursor pulled away by user during hover: $moved times"
    Shot ($Out -replace '\.png$', '-open.png')
    "card hit while open  -> owner pid $(HitOwner $cx 12)"
    "beside card while open -> owner pid $(HitOwner ($cx + 420) 300)"
    [void][P]::SetCursorPos($orig.X, $orig.Y)
    Start-Sleep -Milliseconds 1200
    Shot ($Out -replace '\.png$', '-closed.png')
    "pill hit after close -> owner pid $(HitOwner $cx 12)"
  }
}
