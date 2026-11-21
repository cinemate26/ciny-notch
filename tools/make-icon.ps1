# Draws the Ciny Notch app icon at every size Windows uses and writes one .ico
# (PNG entries) plus a 256 px preview PNG. Run it after changing the design:
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param(
  [string]$Ico = (Join-Path $PSScriptRoot "..\src\CinyNotch\Assets\app.ico"),
  [string]$Preview = (Join-Path $PSScriptRoot "..\src\CinyNotch\Assets\app-256.png")
)
Add-Type -AssemblyName System.Drawing

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 2 * $r
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

function Draw([int]$S) {
  $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
  $k = $S / 256.0
  $small = $S -le 24

  # Glass tile: soft blue to lavender, like light through frosted glass.
  $m = 8 * $k; $side = $S - 2 * $m; $rad = [math]::Max(3, 56 * $k)
  $tile = RoundedRect $m $m $side $side $rad
  $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $S, $S), ([System.Drawing.Color]::FromArgb(255, 150, 186, 255)), ([System.Drawing.Color]::FromArgb(255, 196, 160, 246)))
  $g.FillPath($bg, $tile)

  # Sheen across the upper part, fading out smoothly (a colour blend over the whole tile,
  # so the gradient never repeats and leaves a hard edge).
  $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, ($m - 1)), (New-Object System.Drawing.PointF 0, ($S - $m + 1)), ([System.Drawing.Color]::White), ([System.Drawing.Color]::White))
  $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
  $blend.Colors = @([System.Drawing.Color]::FromArgb(125, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
  $blend.Positions = @([single]0, [single]0.6, [single]1)
  $sheen.InterpolationColors = $blend
  $g.FillPath($sheen, $tile)

  # Rim.
  if (-not $small) {
    $rim = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 255, 255, 255)), ([math]::Max(1, 2.5 * $k))
    $g.DrawPath($rim, $tile)
  }

  # The notch: a black pill across the top.
  if ($small) { $pw = 0.70 * $S; $ph = 0.30 * $S; $py = 0.17 * $S } else { $pw = 132 * $k; $ph = 42 * $k; $py = 34 * $k }
  $px = ($S - $pw) / 2
  $pill = RoundedRect $px $py $pw $ph ($ph / 2)
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 10, 10, 14))), $pill)

  if (-not $small) {
    # A green status dot on the left, a camera lens on the right.
    $dr = 6.5 * $k; $cy = $py + $ph / 2
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 110, 231, 168))), ($px + $ph / 2 - $dr + 4 * $k), ($cy - $dr), (2 * $dr), (2 * $dr))
    $lr = 7.5 * $k
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 34, 44, 66))), ($px + $pw - $ph / 2 - $lr - 4 * $k), ($cy - $lr), (2 * $lr), (2 * $lr))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(160, 120, 150, 210))), ($px + $pw - $ph / 2 - 4 * $k - 2.5 * $k), ($cy - 3.5 * $k), (3 * $k), (3 * $k))
  }

  $g.Dispose()
  return $bmp
}

# Classic icon bitmap (32-bit BGRA, bottom-up, plus an empty AND mask): readable by every
# Windows icon loader. Only the 256 px size is stored as PNG, as Windows itself does.
function DibBytes([System.Drawing.Bitmap]$b) {
  $S = $b.Width
  $rect = New-Object System.Drawing.Rectangle 0, 0, $S, $S
  $data = $b.LockBits($rect, 'ReadOnly', ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
  $pixels = New-Object byte[] ($S * $S * 4)
  [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
  $b.UnlockBits($data)
  $maskRow = [int]([math]::Floor(($S + 31) / 32) * 4)
  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter $ms
  $bw.Write([int]40); $bw.Write([int]$S); $bw.Write([int](2 * $S)); $bw.Write([int16]1); $bw.Write([int16]32)
  $bw.Write([int]0); $bw.Write([int]($S * $S * 4 + $maskRow * $S)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
  for ($row = $S - 1; $row -ge 0; $row--) { $bw.Write($pixels, $row * $S * 4, $S * 4) }
  $bw.Write((New-Object byte[] ($maskRow * $S)))
  $bw.Flush()
  return $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$frames = foreach ($s in $sizes) {
  $b = Draw $s
  if ($s -eq 256) {
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
  } else {
    $bytes = DibBytes $b
  }
  $b.Dispose()
  ,@($s, $bytes)
}

# ICO file: header, one directory entry per size, then the PNG data.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
  $s = $f[0]; $data = $f[1]
  $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
  $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Flush()
New-Item -ItemType Directory -Force (Split-Path $Ico) | Out-Null
[System.IO.File]::WriteAllBytes($Ico, $out.ToArray())
"icon written: $Ico ($([math]::Round($out.Length / 1KB)) KB, sizes $($sizes -join ', '))"
