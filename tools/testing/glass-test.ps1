param([string]$Out, [string]$Exe, [switch]$Closed)
# Shows a bright test pattern behind the island (no focus taken), screenshots the top strip, cleans up.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing @"
using System; using System.Drawing; using System.Windows.Forms; using System.Runtime.InteropServices;
public class Pattern : Form {
  protected override bool ShowWithoutActivation { get { return true; } }
  public Pattern(int x, int w) {
    FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
    StartPosition = FormStartPosition.Manual; Bounds = new Rectangle(x, 0, w, 300);
    Paint += (s, e) => {
      Color[] c = { Color.OrangeRed, Color.Gold, Color.LimeGreen, Color.DeepSkyBlue, Color.MediumPurple, Color.HotPink };
      int bw = w / c.Length;
      for (int i = 0; i < c.Length; i++) e.Graphics.FillRectangle(new SolidBrush(c[i]), i * bw, 0, bw, 300);
      e.Graphics.DrawString("GLASS TEST  GLASS TEST  GLASS TEST", new Font("Segoe UI", 28, FontStyle.Bold), Brushes.White, 20, 60);
    };
  }
}
public static class D {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
  public static string Order(uint islandPid, IntPtr form) {
    var sb = new System.Text.StringBuilder(); int n = 0;
    for (IntPtr h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && n < 400; h = GetWindow(h, 2), n++) {
      if (!IsWindowVisible(h)) continue;
      uint pid; GetWindowThreadProcessId(h, out pid);
      var cls = new System.Text.StringBuilder(128); GetClassName(h, cls, 128);
      string tag = h == form ? "PATTERN" : pid == islandPid ? ("ISLAND:" + cls) : null;
      if (tag != null) sb.Append(n).Append(' ').Append(tag).Append(" | ");
    }
    return sb.ToString();
  }
}
"@
[void][D]::SetProcessDPIAware()
$sw = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width
$cx = [int]($sw / 2)

$env:CINY_DEBUG = '1'
if ($Closed) { Remove-Item Env:CINY_PIN_OPEN -ErrorAction SilentlyContinue } else { $env:CINY_PIN_OPEN = '1' }
$island = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 3

$form = New-Object Pattern ($cx - 450), 900
$form.Show(); [System.Windows.Forms.Application]::DoEvents()
"z-order right after pattern shown: $([D]::Order([uint32]$island.Id, $form.Handle))"
for ($i = 0; $i -lt 25; $i++) { Start-Sleep -Milliseconds 100; [System.Windows.Forms.Application]::DoEvents() }
"z-order at screenshot:            $([D]::Order([uint32]$island.Id, $form.Handle))"

$bmp = New-Object System.Drawing.Bitmap 1000, 330
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($cx - 500, 0, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$a = $bmp.GetPixel(300, 150); $b = $bmp.GetPixel(700, 150)
$sat = [math]::Max([math]::Max($a.R, $a.G), $a.B) - [math]::Min([math]::Min($a.R, $a.G), $a.B)
"card pixels: left rgb($($a.R),$($a.G),$($a.B)) right rgb($($b.R),$($b.G),$($b.B)) -> $(if ($sat -gt 40) { 'GLASS' } else { 'DARK' })"
$g.Dispose(); $bmp.Dispose()

$form.Close(); $form.Dispose()
Stop-Process -Id $island.Id
"island pid $($island.Id) tested; screenshot $Out"
