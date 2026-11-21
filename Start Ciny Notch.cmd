@echo off
rem Starts Ciny Notch from the app folder. Builds it first if the app isn't there yet.
set "APP=%~dp0app\CinyNotch.exe"

if not exist "%APP%" (
  echo The app isn't built yet. Building it now, about a minute...
  call "%~dp0Rebuild and Restart.cmd"
  exit /b
)

start "" "%APP%"
