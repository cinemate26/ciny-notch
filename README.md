# Ciny Notch

A Dynamic Island-style notch for Windows 10 and 11. A glass pill sits at the top centre of your
main screen, shows what matters right now, and opens into a card when you rest the pointer on it.

Free and open source (MIT). Built with C# / .NET 10 and Avalonia.

## What it does

**Closed island (the pill)** shows the most useful thing, in this order: a call timer while
an app uses your microphone, a running timer, Claude Code working (or waiting for you),
music playing (artwork and platform logo), otherwise the time and date. Volume and
brightness changes pop up in it briefly.

**Open island** has six tabs:

| Tab | What's there |
|---|---|
| Home | Clock, mini player when music plays, open apps as small icons (click one to switch to it), weather, quick timer, battery |
| Music | Artwork with platform logo (Spotify, YouTube...), controls, scrub bar, synced lyrics |
| Claude | Every Claude Code session (VS Code or terminal): working, waiting or finished |
| Clipboard | Recent copies: text, links, pictures, files. Pin, remove, click to copy again |
| Reminders | Type one, pick when (5 min to tomorrow 9:00) and repeat; it pops up when due |
| Controls | Volume, brightness, mic mute, night light, screenshot, colour picker |

**Alerts** pop out of the island: reminders (Snooze or Done), timer finished, Claude finished
or waiting for you.

## Shortcuts

| Keys | Does |
|---|---|
| Ctrl+Alt+N | Open or close the island |
| Ctrl+Alt+P | Play or pause |
| Ctrl+Alt+Shift+M | Mute every microphone (Ctrl+Alt+M if no other app owns it; Settings shows which) |
| Ctrl+Alt+H | Hide or show the island |
| Ctrl+Alt+S | Screenshot (Snipping Tool) |
| Ctrl+Alt+C | Copy the colour under the pointer |

## Build and run

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) (x64).
2. Optional, for the intended look: download **Satoshi** free from
   [fontshare.com](https://www.fontshare.com/fonts/satoshi) and copy `Satoshi-Light.otf` and
   `Satoshi-Regular.otf` into `src/CinyNotch/Assets/Fonts/`. Without them the app uses the
   Windows UI font. The fonts aren't included here because their licence doesn't allow
   redistribution.
3. Double-click `Rebuild and Restart.cmd`. It builds one self-contained `app\CinyNotch.exe`
   (no install, .NET not needed to run it) and starts it. `Start Ciny Notch.cmd` starts it later.
4. Optional: `powershell -ExecutionPolicy Bypass -File tools\install.ps1` adds **Ciny Notch** and
   **Close Ciny Notch** to the Start menu and turns on start with Windows.

`Rebuild and Restart.cmd` expects the SDK at `C:\Program Files\dotnet`. For a quick compile
check: `dotnet build src/CinyNotch/CinyNotch.csproj -c Release`.

## Start, close, settings

- **Start:** search **Ciny Notch** in the Start menu, or double-click `Start Ciny Notch.cmd`.
  Running it again while it's open just opens the island.
- **Close:** search **Close Ciny Notch**, right-click the tray icon and choose **Quit Ciny Notch**,
  or run `CinyNotch.exe --quit`.
- **Settings:** click the tray icon. Shape (notch or floating), look (glass, dark, black),
  weather city, synced lyrics, Claude Code status and alerts, start with Windows, clear
  clipboard history.

## Where things are

| Folder | Holds |
|---|---|
| `src/CinyNotch/` | The source code |
| `tools/` | `install.ps1` (Start menu + autostart), `make-icon.ps1` (draws the icon), `testing/` (screenshot tests) |
| `app/` | The built app (created by `Rebuild and Restart.cmd`, not in the repo) |
| `data/` | Your settings, clipboard history, reminders (created on first run, never leaves your PC) |

## Privacy

Nothing leaves the PC except lyrics lookups (song title, artist and album to
[lrclib.net](https://lrclib.net), can be turned off) and weather (the city name to
[open-meteo.com](https://open-meteo.com), only if you set one). Clipboard history, reminders
and settings live in `data\` next to the app. Copies that password managers mark as private
are never recorded. The Claude tab only reads the end of Claude Code's local session logs in
`%UserProfile%\.claude\projects`.

## Good to know

- Mirroring Windows notifications into the island needs Windows Developer Mode, so it isn't included.
- The blur strength is set by Windows. The glass tint adapts: clear over dark pages, a little
  darker over bright ones so white text stays readable.
- The app is unsigned, so Windows SmartScreen may warn the first time you run a build.

## Credits

- [Avalonia](https://avaloniaui.net) (MIT) for the UI.
- [LRCLIB](https://lrclib.net) for synced lyrics, [Open-Meteo](https://open-meteo.com) for weather.
- Satoshi by [Indian Type Foundry](https://www.fontshare.com) (ITF Free Font License, not included).
- App and platform names and logos belong to their owners; they're shown only to identify
  what's playing or running.

## Licence

MIT. See [LICENSE](LICENSE).
