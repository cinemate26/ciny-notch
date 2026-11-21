@echo off
rem Closes Ciny Notch, rebuilds it from src into the app folder (one self-contained .exe), and starts it.
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
set DOTNET_CLI_TELEMETRY_OPTOUT=1

taskkill /im CinyNotch.exe /f >nul 2>&1
echo Building Ciny Notch...
"%DOTNET%" publish "%~dp0src\CinyNotch\CinyNotch.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None -p:DebugSymbols=false ^
  -o "%~dp0app" -v q -nologo
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
rem Debug symbol files aren't needed to run the app.
del /q "%~dp0app\*.pdb" >nul 2>&1

start "" "%~dp0app\CinyNotch.exe"
