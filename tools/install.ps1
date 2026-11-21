# Makes Ciny Notch findable in Start search ("Ciny Notch" opens it, "Close Ciny Notch" quits it)
# and turns on start with Windows. Safe to run again; it just refreshes the shortcuts.
#   powershell -ExecutionPolicy Bypass -File tools\install.ps1
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "app\CinyNotch.exe"
if (-not (Test-Path $exe)) { throw "App not built yet: run 'Rebuild and Restart.cmd' first." }

$programs = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shell = New-Object -ComObject WScript.Shell

$open = $shell.CreateShortcut((Join-Path $programs "Ciny Notch.lnk"))
$open.TargetPath = $exe
$open.WorkingDirectory = Split-Path $exe
$open.IconLocation = "$exe,0"
$open.Description = "Open Ciny Notch, the island at the top of the screen"
$open.Save()

$close = $shell.CreateShortcut((Join-Path $programs "Close Ciny Notch.lnk"))
$close.TargetPath = $exe
$close.Arguments = "--quit"
$close.WorkingDirectory = Split-Path $exe
$close.IconLocation = "$exe,0"
$close.Description = "Close Ciny Notch"
$close.Save()

# Start with Windows (per user, no admin needed). The app also keeps this up to date itself.
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "CinyNotch" -Value "`"$exe`""

"Start menu: 'Ciny Notch' and 'Close Ciny Notch' created in $programs"
"Start with Windows: on ($exe)"
