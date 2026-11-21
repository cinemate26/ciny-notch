param([string]$OutDir, [string]$Exe)
# Island opens/closes by itself (CINY_TEST_CYCLE) over a bright pattern; grabs a burst of frames
# of the top strip and lays them out as one contact sheet. Cursor and focus are never touched.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing @"
using System; using System.Drawing; using System.Windows.Forms; using System.Runtime.InteropServices;
public class Pattern2 : Form {
  protected override bool ShowWithoutActivation { get { return true; } }
  public Pattern2(int x, int w) {
    FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
    StartPosition = FormStartPosition.Manual; Bounds = new Rectangle(x, 0, w, 300);
    Paint += (s, e) => {
      Color[] c = { Color.OrangeRed, Color.Gold, Color.LimeGreen, Color.DeepSkyBlue, Color.MediumPurple, Color.HotPink };
      int bw = w / c.Length;
      for (int i = 0; i < c.Length; i++) e.Graphics.FillRectangle(new SolidBrush(c[i]), i * bw, 0, bw, 300);
    };
  }
}
public static class D2 { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[void][D2]::SetProcessDPIAware()
$sw = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width
$cx = [int]($sw / 2)
New-Item -ItemType Directory -Force $OutDir | Out-Null

$form = New-Object Pattern2 ($cx - 450), 900
$form.Show(); [System.Windows.Forms.Application]::DoEvents()

$env:CINY_DEBUG = '1'; $env:CINY_TEST_CYCLE = '1'
Remove-Item (Join-Path $PSScriptRoot "..\..\data\debug.log") -ErrorAction SilentlyContinue
$island = Start-Process -FilePath $Exe -PassThru
Remove-Item Env:CINY_TEST_CYCLE

# Wait for the first open (about 1.5 s after the window opens), then burst-capture through it.
Start-Sleep -Milliseconds 2600
$frames = New-Object System.Collections.Generic.List[System.Drawing.Bitmap]
$sw2 = [Diagnostics.Stopwatch]::StartNew()
while ($sw2.ElapsedMilliseconds -lt 1800) {
  $b = New-Object System.Drawing.Bitmap 700, 230
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($cx - 350, 0, 0, 0, $b.Size); $g.Dispose()
  $frames.Add($b)
  [System.Windows.Forms.Application]::DoEvents()
}
"captured $($frames.Count) frames in $($sw2.ElapsedMilliseconds) ms"

# Contact sheet: 4 columns at half size.
$cols = 4; $tw = 350; $th = 115
$rows = [math]::Ceiling($frames.Count / $cols)
$sheet = New-Object System.Drawing.Bitmap ($cols * $tw), ($rows * $th)
$sg = [System.Drawing.Graphics]::FromImage($sheet)
for ($i = 0; $i -lt $frames.Count; $i++) {
  $sg.DrawImage($frames[$i], ($i % $cols) * $tw, [math]::Floor($i / $cols) * $th, $tw, $th)
  $frames[$i].Save((Join-Path $OutDir ("f{0:D2}.png" -f $i)), [System.Drawing.Imaging.ImageFormat]::Png)
}
$sheet.Save((Join-Path $OutDir "sheet.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$sg.Dispose(); $sheet.Dispose()

Stop-Process -Id $island.Id
$form.Close(); $form.Dispose()
Remove-Item Env:CINY_DEBUG
