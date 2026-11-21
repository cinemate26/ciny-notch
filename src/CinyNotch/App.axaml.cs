using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CinyNotch.Island;
using CinyNotch.Island.Panels;
using CinyNotch.Native;
using CinyNotch.Services;
using CinyNotch.Settings;

namespace CinyNotch;

public partial class App : Application
{
    private static class Keys
    {
        public const int Toggle = 1, PlayPause = 2, Mic = 3, Hide = 4, Screenshot = 5, Colour = 6;
    }

    private IslandWindow? _island;
    private Hub? _hub;
    private MessageWindow? _messages;
    private SettingsWindow? _settingsWindow;
    private NativeMenuItem? _hideItem;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _hub = new Hub { Settings = AppSettings.Load() };
            _hub.ApplySettings();

            _island = new IslandWindow(_hub);
            _island.Show();

            StartServices(_hub, _island);
            SetUpShortcuts(_hub, _island);
            SetUpTray(desktop);
            SetUpAutostart(_hub.Settings);
            ListenForSignals(desktop, _island);

            // Debug only: CINY_OPEN_SETTINGS=1 opens Settings at start, for screenshots.
            if (Environment.GetEnvironmentVariable("CINY_OPEN_SETTINGS") == "1") OpenSettings();

            desktop.Exit += (_, _) =>
            {
                _messages?.Dispose();
                _hub.Brightness.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Starts with Windows by default (turned on the first time the app runs; Settings can turn
    /// it off), and keeps the startup entry pointing at wherever the app lives now.
    /// </summary>
    private static void SetUpAutostart(AppSettings settings)
    {
        try
        {
            if (!settings.AutostartSetUp)
            {
                StartWithWindows.Set(true);
                settings.AutostartSetUp = true;
                settings.Save();
            }
            else if (StartWithWindows.IsOn)
            {
                StartWithWindows.Set(true); // refresh the path in case the app moved
            }
        }
        catch
        {
            // Without access to the Run key the app still works; it just won't autostart.
        }
    }

    /// <summary>
    /// Launching the app again (from Start search, for example) opens the island; the
    /// "Close Ciny Notch" shortcut runs "CinyNotch.exe --quit", which closes it.
    /// </summary>
    private static void ListenForSignals(IClassicDesktopStyleApplicationLifetime desktop, IslandWindow island)
    {
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitSignal);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSignal);
        var thread = new Thread(() =>
        {
            while (true)
            {
                var which = WaitHandle.WaitAny([quit, show]);
                if (which == 0)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
                    return;
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(island.ShowFromLaunch);
            }
        })
        { IsBackground = true, Name = "CinyNotch signals" };
        thread.Start();
    }

    private static void StartServices(Hub hub, IslandWindow island)
    {
        hub.Audio.Start();
        hub.MicUse.Start();
        hub.Claude.Start();
        hub.Brightness.Start();
        hub.Clipboard.Attach(island);
        _ = hub.Media.StartAsync();
        _ = hub.Radios.StartAsync();
    }

    private void SetUpShortcuts(Hub hub, IslandWindow island)
    {
        _messages = new MessageWindow();
        const MessageWindow.Mod ctrlAlt = MessageWindow.Mod.Ctrl | MessageWindow.Mod.Alt;
        _messages.RegisterHotKey(Keys.Toggle, ctrlAlt, 0x4E);     // N
        _messages.RegisterHotKey(Keys.PlayPause, ctrlAlt, 0x50);  // P
        // M. Some apps already own Ctrl+Alt+M; fall back to Ctrl+Alt+Shift+M.
        if (!_messages.RegisterHotKey(Keys.Mic, ctrlAlt, 0x4D))
        {
            hub.MicShortcut = _messages.RegisterHotKey(Keys.Mic, ctrlAlt | MessageWindow.Mod.Shift, 0x4D)
                ? "Ctrl+Alt+Shift+M"
                : "none (taken by another app)";
        }
        _messages.RegisterHotKey(Keys.Hide, ctrlAlt, 0x48);       // H
        _messages.RegisterHotKey(Keys.Screenshot, ctrlAlt, 0x53); // S
        _messages.RegisterHotKey(Keys.Colour, ctrlAlt, 0x43);     // C

        _messages.HotKey += id =>
        {
            switch (id)
            {
                case Keys.Toggle: island.ToggleOpen(); break;
                case Keys.PlayPause: _ = hub.Media.PlayPauseAsync(); break;
                case Keys.Mic: island.ShowMicState(hub.Audio.ToggleMicMute()); break;
                case Keys.Hide: island.ToggleHidden(); UpdateHideItem(); break;
                case Keys.Screenshot: ControlsPanel.Launch("ms-screenclip:"); break;
                case Keys.Colour: island.PickColour(); break;
            }
        };

        _messages.ListenToClipboard();
        _messages.ClipboardChanged += hub.Clipboard.OnClipboardChanged;
    }

    private void SetUpTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var settings = new NativeMenuItem("Settings...");
        settings.Click += (_, _) => OpenSettings();

        _hideItem = new NativeMenuItem("Hide island");
        _hideItem.Click += (_, _) => { _island?.ToggleHidden(); UpdateHideItem(); };

        var quit = new NativeMenuItem("Quit Ciny Notch");
        quit.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Add(settings);
        menu.Add(_hideItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quit);

        var tray = new TrayIcon
        {
            Icon = MakeTrayIcon(),
            ToolTipText = "Ciny Notch",
            Menu = menu,
            IsVisible = true,
        };
        tray.Clicked += (_, _) => OpenSettings();
        TrayIcon.SetIcons(this, new TrayIcons { tray });
    }

    private void UpdateHideItem()
    {
        if (_hideItem is not null && _island is not null)
            _hideItem.Header = _island.HiddenByUser ? "Show island" : "Hide island";
    }

    private void OpenSettings()
    {
        if (_hub is null || _island is null) return;
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_hub, _island.SettingsChanged) { Icon = MakeTrayIcon() };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>The notch icon (Assets\app.ico), shared by the tray and the Settings window.</summary>
    private static WindowIcon MakeTrayIcon()
    {
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://CinyNotch/Assets/app.ico"));
            return new WindowIcon(stream);
        }
        catch
        {
            // Fallback: a small white pill drawn in code.
            var bitmap = new RenderTargetBitmap(new PixelSize(32, 32));
            using (var ctx = bitmap.CreateDrawingContext())
            {
                ctx.DrawRectangle(Brushes.White, null, new RoundedRect(new Rect(3, 11, 26, 10), 5));
            }
            return new WindowIcon(bitmap);
        }
    }
}
