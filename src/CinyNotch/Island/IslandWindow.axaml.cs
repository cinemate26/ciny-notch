using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CinyNotch.Diagnostics;
using CinyNotch.Island.Panels;
using CinyNotch.Native;
using CinyNotch.Services;
using CinyNotch.Settings;
using CinyNotch.Ui;
using U = CinyNotch.Ui.Ui;

namespace CinyNotch.Island;

internal partial class IslandWindow : Window, IIslandHost
{
    // Sizes in device-independent pixels.
    private const double CardW = 560;
    private const double PeekH = 34;
    private const double SwellExtraW = 16, SwellExtraH = 4;
    private const double AlertW = 400, AlertH = 96;
    private const double OpenRadius = 26;
    private const double FloatingGap = 8;

    // How long the pointer must rest before the island opens, and how long it may
    // leave before the island closes again.
    private static readonly TimeSpan OpenDwell = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(280);

    private enum Mode { Collapsed, Expanded, Alert }
    private enum PeekKind { Default, Music, Claude, Call, Osd, Timer }

    private readonly Hub _hub;
    private readonly List<IslandPanel> _panels = new();
    private IslandPanel _panel = null!;

    private readonly Spring _w = new(220);
    private readonly Spring _h = new(PeekH);
    private readonly DispatcherTimer _pointerTimer;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _screenTimer;
    private readonly TranslateTransform _cardShift = new();
    private readonly ScaleTransform _cardScale = new();
    private readonly Random _random = new();

    private GlassBackdrop? _glass;
    private IslandShape _shape;
    private IslandLook _look;
    private readonly IBrush _glassTint;
    private readonly GradientStop _tintTop = new(Color.Parse("#21262630"), 0);
    private readonly GradientStop _tintBottom = new(Color.Parse("#38121218"), 1);
    private double _tintLevel = 0.15;
    private nint _hwnd;
    private Mode _mode = Mode.Collapsed;
    private bool _swollen;
    private bool _animating;
    private bool _clickThrough;
    private bool _hiddenForFullscreen;
    private bool _hiddenByUser;
    private bool _keyboard;
    private bool _openedByKey;
    private bool _pinnedOpen;
    private TimeSpan? _lastFrame;
    private DateTime? _hoverSince;
    private DateTime? _outsideSince;

    // Peeks.
    private PeekKind _peek = PeekKind.Default;
    private double _peekW = 220;
    private DateTime _transientUntil;
    private readonly Grid _peekDefault = PeekGrid();
    private readonly TextBlock _peekTime = U.Text("", 13);
    private readonly TextBlock _peekDate = U.Text("", 12, U.Secondary);
    private readonly Grid _peekMusic = PeekGrid();
    private readonly Image _peekArt = new() { Width = 22, Height = 22, Stretch = Stretch.UniformToFill };
    private readonly Border _peekArtFrame = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = U.Brush("#26FFFFFF") };
    private readonly TextBlock _peekSong = U.Text("", 12, U.Secondary);
    private readonly StackPanel _peekBars = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _peekPlatform = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _peekClaude = PeekGrid();
    private readonly Image _peekClaudeLogo = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _peekClaudeDot = U.Dot(U.Green, 7);
    private readonly TextBlock _peekClaudeTitle = U.Text("", 12);
    private readonly TextBlock _peekClaudeState = U.Text("", 12, U.Secondary);
    private readonly Grid _peekCall = PeekGrid();
    private readonly TextBlock _peekCallTime = U.Text("", 13);
    private readonly TextBlock _peekCallApp = U.Text("", 12, U.Secondary);
    private readonly TextBlock _peekCallMic = U.Icon(Glyph.Mic, 12);
    private readonly Grid _peekOsd = PeekGrid();
    private readonly TextBlock _peekOsdIcon = U.Icon(Glyph.Volume, 13);
    private readonly Border _peekOsdTrack = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = U.Brush("#33FFFFFF"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
    private readonly Border _peekOsdFill = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = U.Brush("#F2F2F4"), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _peekOsdText = U.Text("", 12, U.Secondary);
    private readonly Border _peekOsdSwatch = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly Grid _peekTimer = PeekGrid();
    private readonly TextBlock _peekTimerText = U.Text("", 13);

    // Alert.
    private Alert? _alert;
    private DateTime _alertUntil;

    public IslandWindow() : this(new Hub { Settings = new AppSettings() }) { }

    internal IslandWindow(Hub hub)
    {
        InitializeComponent();
        _hub = hub;
        _shape = hub.Settings.Shape;
        _look = hub.Settings.Look;
        // The glass tint adapts to what's behind it (see AdaptTint), so it's built here.
        _glassTint = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { _tintTop, _tintBottom },
        };
        Island.Background = _glassTint;
        SetTint(0.15);

        // The card scales with the island from its top centre, so a growing or shrinking
        // island never cuts its text off at the edges.
        Card.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        Card.RenderTransform = new TransformGroup { Children = { _cardScale, _cardShift } };
        AlertCard.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);

        BuildPeeks();
        BuildPanels();

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Collapse(); };
        Deactivated += (_, _) => LeaveKeyboard();

        // ~30 pointer checks a second: cheap, and fast enough to feel instant.
        _pointerTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => CheckPointer());
        // Four times a second: progress bars, timers, the music peek.
        _tickTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
        // Once a second: clock, reminders, fullscreen and stay-on-top.
        _screenTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => EverySecond());

        WireServices();

        Opened += (_, _) => OnOpenedOnce();
        Closed += (_, _) =>
        {
            _pointerTimer.Stop();
            _tickTimer.Stop();
            _screenTimer.Stop();
            _glass?.Dispose();
        };
    }

    // =====================================================================
    // Setup
    // =====================================================================

    private void BuildPanels()
    {
        _panels.Add(new HomePanel(_hub, this));
        _panels.Add(new MusicPanel(_hub, this));
        _panels.Add(new ClaudePanel(_hub, this));
        _panels.Add(new ClipboardPanel(_hub, this));
        _panels.Add(new RemindersPanel(_hub, this));
        _panels.Add(new ControlsPanel(_hub, this, PickColour));

        foreach (var p in _panels)
        {
            p.IsVisible = false;
            ContentHost.Children.Add(p);
            var tab = new Border { Tag = p.TabName, Child = new TextBlock { Text = p.TabName } };
            tab.Classes.Add("tab");
            tab.Tapped += (_, _) => SelectTab(p.TabName);
            Tabs.Children.Add(tab);
        }
        ShowPanel(_panels[0]);
    }

    private static Grid PeekGrid() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        Height = PeekH,
        Margin = new Thickness(16, 0),
        IsVisible = false,
    };

    private void BuildPeeks()
    {
        Place(_peekDefault, _peekTime, null, _peekDate);

        _peekArtFrame.Child = _peekArt;
        for (var i = 0; i < 4; i++)
            _peekBars.Children.Add(new Border { Width = 3, Height = 6, CornerRadius = new CornerRadius(1.5), Background = U.Brush("#F2F2F4"), VerticalAlignment = VerticalAlignment.Center });
        _peekSong.Margin = new Thickness(10, 0);
        Place(_peekMusic, _peekArtFrame, _peekSong, U.Row(8, _peekBars, _peekPlatform));

        // Claude's own logo, from the installed Claude Code extension.
        _peekClaudeLogo.Source = AppIcons.Claude;
        _peekClaudeTitle.Margin = new Thickness(10, 0);
        var claudeMark = _peekClaudeLogo.Source is null
            ? (Control)U.Text("Claude", 12, U.Primary, FontWeight.Regular)
            : new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = _peekClaudeLogo };
        Place(_peekClaude, claudeMark, _peekClaudeTitle, U.Row(6, _peekClaudeDot, _peekClaudeState));

        _peekCallApp.Margin = new Thickness(10, 0);
        Place(_peekCall, U.Row(8, U.Dot(U.Red, 7), _peekCallTime), _peekCallApp, _peekCallMic);

        _peekOsdTrack.Child = _peekOsdFill;
        Place(_peekOsd, U.Row(8, _peekOsdIcon, _peekOsdSwatch), _peekOsdTrack, _peekOsdText);

        Place(_peekTimer, U.Row(8, U.Icon(Glyph.Timer, 12, U.Secondary), U.Text("Timer", 12, U.Secondary)), null, _peekTimerText);

        foreach (var g in new[] { _peekDefault, _peekMusic, _peekClaude, _peekCall, _peekOsd, _peekTimer })
            PeekHost.Children.Add(g);
        _peekDefault.IsVisible = true;
    }

    private static void Place(Grid g, Control left, Control? middle, Control right)
    {
        left.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(left);
        if (middle is not null)
        {
            middle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(middle, 1);
            g.Children.Add(middle);
        }
        right.VerticalAlignment = VerticalAlignment.Center;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
    }

    private void WireServices()
    {
        _hub.Media.Changed += UpdatePeek;
        _hub.Claude.Changed += UpdatePeek;
        _hub.MicUse.Changed += UpdatePeek;
        _hub.Timer.Changed += UpdatePeek;
        _hub.Audio.Changed += UpdatePeek;

        _hub.Audio.VolumeChangedOutside += (level, muted) =>
            ShowOsd(muted ? Glyph.Mute : Glyph.Volume, muted ? "Muted" : $"{Math.Round(level * 100)}%", muted ? 0 : level);
        _hub.Brightness.ChangedOutside += level => ShowOsd(Glyph.Brightness, $"{level}%", level / 100.0);

        _hub.Reminders.Due += r =>
        {
            Reminder current = r;
            ShowAlert(new Alert(Glyph.Ringer, r.Text, RemindersPanel.DueLabel(r) == "Now" ? "Reminder" : RemindersPanel.DueLabel(r),
                [
                    new AlertButton("Snooze 10 min", () => _hub.Reminders.Snooze(current, TimeSpan.FromMinutes(10))),
                    new AlertButton("Done", () => _hub.Reminders.Done(current), Primary: true),
                ],
                TimeSpan.FromMinutes(2), U.Amber, Beep: true));
        };

        _hub.Timer.Finished += () => ShowAlert(new Alert(Glyph.Timer, "Time's up",
            $"Your {Math.Round(_hub.Timer.Length.TotalMinutes)} minute timer is done.",
            [new AlertButton("OK", () => { }, Primary: true)], TimeSpan.FromSeconds(30), U.Amber, Beep: true));

        _hub.Claude.Finished += s =>
        {
            if (!_hub.Settings.ClaudeAlerts) return;
            ShowAlert(new Alert(Glyph.Accept, "Claude finished", $"{s.Title}  ·  {s.Project}",
                [new AlertButton("OK", () => { }, Primary: true)], TimeSpan.FromSeconds(7), U.Green, Logo: AppIcons.Claude));
        };
        _hub.Claude.NeedsYou += s =>
        {
            if (!_hub.Settings.ClaudeAlerts) return;
            ShowAlert(new Alert(Glyph.Warning, "Claude may need you", $"{s.Title}  ·  {s.Project}",
                [new AlertButton("OK", () => { }, Primary: true)], TimeSpan.FromSeconds(8), U.Amber, Logo: AppIcons.Claude));
        };
    }

    private void OnOpenedOnce()
    {
        _hwnd = TryGetPlatformHandle()?.Handle ?? 0;
        Log.Write($"opened hwnd {_hwnd:X}");
        if (_hwnd != 0)
        {
            // Tool window: no taskbar button, no Alt+Tab. No-activate: never steals focus.
            // Layered + transparent: clicks pass through to whatever is underneath.
            Win32.SetExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED, Win32.WS_EX_APPWINDOW);
            Win32.SetLayeredWindowAttributes(_hwnd, 0, 255, Win32.LWA_ALPHA);
        }
        SetClickThrough(true);
        PlaceOnScreen();

        var (x, y, w, h) = WindowRectPx();
        _glass = GlassBackdrop.TryCreate(x, y, w, h);
        _glass?.StackBelow(_hwnd);

        ApplyShape(_shape);
        ApplyLook(_look);
        UpdatePeek();
        Screens.Changed += (_, _) => PlaceOnScreen();

        _pointerTimer.Start();
        _tickTimer.Start();
        _screenTimer.Start();

        // Debug only: CINY_PIN_OPEN=<tab> opens the island on that tab and keeps it open, for screenshots.
        var pin = Environment.GetEnvironmentVariable("CINY_PIN_OPEN");
        if (!string.IsNullOrEmpty(pin))
        {
            _pinnedOpen = true;
            if (pin != "1") SelectTab(pin);
            Expand();
        }

        // Debug only: CINY_TEST_CYCLE=1 opens and closes the island every 1.5 s, for checking motion.
        if (Environment.GetEnvironmentVariable("CINY_TEST_CYCLE") == "1")
        {
            _pinnedOpen = true;
            new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Normal,
                (_, _) => { if (_mode == Mode.Expanded) Collapse(); else Expand(); }).Start();
        }

        if (Log.On) Dispatcher.UIThread.Post(LogTabRects, DispatcherPriority.Background);
    }

    private (int X, int Y, int W, int H) WindowRectPx()
    {
        var s = RenderScaling;
        return (Position.X, Position.Y, (int)Math.Round(Width * s), (int)Math.Round(Height * s));
    }

    /// <summary>Top centre of the primary monitor.</summary>
    private void PlaceOnScreen()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var widthPx = (int)Math.Round(Width * screen.Scaling);
        Position = new PixelPoint(screen.Bounds.X + (screen.Bounds.Width - widthPx) / 2, screen.Bounds.Y);

        if (_glass is not null)
        {
            var (x, y, w, h) = WindowRectPx();
            _glass.MoveTo(x, y, w, h);
        }
    }

    public void ApplyShape(IslandShape shape)
    {
        _shape = shape;
        Island.Margin = shape == IslandShape.Floating ? new Thickness(0, FloatingGap, 0, 0) : default;
        Island.BorderThickness = shape == IslandShape.Floating ? new Thickness(1) : new Thickness(1, 0, 1, 1);
        ApplyFrame();
    }

    public void ApplyLook(IslandLook look)
    {
        _look = look;
        var glass = look == IslandLook.Glass && _glass is not null;
        Island.Background = look switch
        {
            IslandLook.Black => Brushes.Black,
            IslandLook.Dark => U.Brush("#F5141418"),
            _ => _glass is null ? U.Brush("#F00D0D10") : _glassTint,
        };
        Sheen.IsVisible = look != IslandLook.Black;
        Frost.IsVisible = look == IslandLook.Glass;
        _glass?.SetVisible(glass && Root.IsVisible);
    }

    /// <summary>Re-reads settings after the Settings window saves.</summary>
    internal void SettingsChanged()
    {
        ApplyShape(_hub.Settings.Shape);
        ApplyLook(_hub.Settings.Look);
        foreach (var p in _panels) p.OnShown();
        UpdatePeek();
    }

    // =====================================================================
    // Tabs
    // =====================================================================

    public void SelectTab(string name)
    {
        var panel = _panels.FirstOrDefault(p => p.TabName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (panel is null) return;
        ShowPanel(panel);
        if (_mode == Mode.Expanded)
        {
            _h.Target = _panel.CardHeight;
            StartAnimation();
        }
        Log.Write($"tab {panel.TabName}");
    }

    private void ShowPanel(IslandPanel panel)
    {
        _panel = panel;
        foreach (var p in _panels) p.IsVisible = p == panel;
        foreach (var tab in Tabs.Children.OfType<Border>())
            tab.Classes.Set("on", (tab.Tag as string) == panel.TabName);
        Card.Height = panel.CardHeight;
        if (_mode == Mode.Expanded) panel.OnShown();
    }

    public void PanelResized()
    {
        Card.Height = _panel.CardHeight;
        if (_mode == Mode.Expanded) { _h.Target = _panel.CardHeight; StartAnimation(); }
    }

    private void LogTabRects()
    {
        var s = RenderScaling;
        foreach (var tab in Tabs.Children.OfType<Border>())
        {
            var p = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), Card);
            if (p is { } c) Log.Write($"tabpos {tab.Tag} {(int)((c.X + (Width - CardW) / 2) * s)} {(int)(c.Y * s)}");
        }
    }

    // =====================================================================
    // Pointer: hover to open, leave to close
    // =====================================================================

    private void CheckPointer()
    {
        if (_hwnd == 0 || IsHidden || _pinnedOpen) return;
        if (!Win32.GetCursorPos(out var p)) return;

        var rect = IslandRectPx(_mode == Mode.Collapsed ? 2 : 6);
        var over = rect.Contains(new PixelPoint(p.X, p.Y));
        var now = DateTime.UtcNow;

        var button = Win32.LeftButtonDown();

        switch (_mode)
        {
            case Mode.Collapsed:
                if (!over)
                {
                    _hoverSince = null;
                    if (_swollen) SetSwell(false);
                    return;
                }
                if (button) return; // a click or drag on whatever is under the island: leave it alone
                _hoverSince ??= now;
                if (!_swollen) SetSwell(true);
                if (now - _hoverSince >= OpenDwell) Expand();
                return;

            case Mode.Alert:
                SetClickThrough(!over);
                if (over) _alertUntil = DateTime.Now.AddSeconds(Math.Max(2, (_alertUntil - DateTime.Now).TotalSeconds));
                return;

            case Mode.Expanded:
                if (_keyboard) { SetClickThrough(false); return; }
                if (_openedByKey)
                {
                    if (over) _openedByKey = false; // the pointer has taken over
                    else return;
                }
                // While open, only the card itself takes clicks; the empty space around it lets them through.
                SetClickThrough(!over);
                if (over) { _outsideSince = null; return; }
                _outsideSince ??= now;
                if (now - _outsideSince >= CloseDelay) Collapse();
                return;
        }
    }

    private PixelRect IslandRectPx(double inflateDip)
    {
        var (x, y, w, h) = IslandRectDip();
        var r = new Rect(x, y, w, h).Inflate(inflateDip);
        var s = RenderScaling;
        return new PixelRect(
            Position.X + (int)Math.Floor(r.X * s),
            Position.Y + (int)Math.Floor(r.Y * s),
            (int)Math.Ceiling(r.Width * s),
            (int)Math.Ceiling(r.Height * s));
    }

    /// <summary>
    /// The island's current rectangle inside the window, from the springs, snapped to the
    /// pixel grid: width to an even number of pixels and height to whole pixels. That keeps
    /// the centred island and its centred content on exact pixels every frame.
    /// </summary>
    private (double X, double Y, double W, double H) IslandRectDip()
    {
        var s = RenderScaling;
        var w = Math.Round(Math.Max(_w.Value, 40) * s / 2) * 2 / s;
        var h = Math.Round(Math.Max(_h.Value, 20) * s) / s;
        var top = _shape == IslandShape.Floating ? FloatingGap : 0;
        return ((Width - w) / 2, top, w, h);
    }

    // =====================================================================
    // Open, close, alerts
    // =====================================================================

    private void SetSwell(bool on)
    {
        _swollen = on;
        _w.Target = _peekW + (on ? SwellExtraW : 0);
        _h.Target = PeekH + (on ? SwellExtraH : 0);
        StartAnimation();
    }

    private void Expand()
    {
        _mode = Mode.Expanded;
        _swollen = false;
        _hoverSince = null;
        _outsideSince = null;
        AlertCard.IsVisible = false;
        Card.IsVisible = true;
        SetClickThrough(false);
        _panel.OnShown();
        _w.Target = CardW;
        _h.Target = _panel.CardHeight;
        StartAnimation();
        // Measure around the card's final size once it has grown.
        DispatcherTimer.RunOnce(AdaptTint, TimeSpan.FromMilliseconds(350));
    }

    private void Collapse()
    {
        LeaveKeyboard();
        _mode = Mode.Collapsed;
        _outsideSince = null;
        _openedByKey = false;
        SetClickThrough(true);
        _w.Target = _peekW;
        _h.Target = PeekH;
        StartAnimation();
        UpdatePeek();
    }

    /// <summary>Ctrl+Alt+N: open on the last tab, or close.</summary>
    internal void ToggleOpen()
    {
        if (IsHidden) return;
        if (_mode == Mode.Expanded) { Collapse(); return; }
        _openedByKey = true;
        Expand();
    }

    /// <summary>Ctrl+Alt+H: hide the island completely, or bring it back.</summary>
    internal void ToggleHidden()
    {
        _hiddenByUser = !_hiddenByUser;
        if (_hiddenByUser && _mode != Mode.Collapsed) Collapse();
        RefreshVisibility();
    }

    internal bool HiddenByUser => _hiddenByUser;

    /// <summary>The app was launched again (Start search): bring the island back and open it.</summary>
    internal void ShowFromLaunch()
    {
        if (_hiddenByUser) ToggleHidden();
        if (_mode != Mode.Expanded)
        {
            SelectTab("Home");
            _openedByKey = true;
            Expand();
        }
    }

    void IIslandHost.Close() => Collapse();

    public void ShowAlert(Alert alert)
    {
        if (_hiddenByUser) return;
        // In a fullscreen game or video, only reminders and timers interrupt.
        if (_hiddenForFullscreen && !alert.Beep) return;

        _alert = alert;
        _alertUntil = DateTime.Now + alert.ShowFor;
        BuildAlert(alert);
        if (alert.Beep) Win32.MessageBeep(0x40); // MB_ICONASTERISK

        LeaveKeyboard();
        _mode = Mode.Alert;
        Card.IsVisible = false;
        AlertCard.IsVisible = true;
        SetClickThrough(true);
        RefreshVisibility();
        _w.Target = AlertW;
        _h.Target = AlertH;
        StartAnimation();
    }

    private void BuildAlert(Alert alert)
    {
        AlertCard.Children.Clear();
        AlertCard.ColumnDefinitions = new ColumnDefinitions("Auto,*");
        AlertCard.Margin = new Thickness(0);

        Control mark = alert.Logo is { } logo
            ? new Border { CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = new Image { Source = logo, Stretch = Stretch.UniformToFill } }
            : U.Icon(alert.Glyph, 16, alert.GlyphColor);
        var badge = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(alert.Logo is null ? 19 : 10),
            Background = alert.Logo is null ? U.Brush("#1FFFFFFF") : Brushes.Transparent,
            Child = mark,
            Margin = new Thickness(18, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AlertCard.Children.Add(badge);

        var title = U.Text(alert.Title, 15, U.Primary, FontWeight.Regular);
        var subtitle = U.Text(alert.Subtitle, 12, U.Secondary);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var b in alert.Buttons)
        {
            var chip = U.Chip(b.Text, () => { b.OnClick(); DismissAlert(); });
            if (b.Primary) chip.Classes.Add("on");
            buttons.Children.Add(chip);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0), Children = { title, subtitle } };
        if (alert.Buttons.Count > 0) text.Children.Add(buttons);
        Grid.SetColumn(text, 1);
        AlertCard.Children.Add(text);
    }

    private void DismissAlert()
    {
        if (_mode != Mode.Alert) return;
        _alert = null;
        Collapse();
        RefreshVisibility();
    }

    // =====================================================================
    // Keyboard: only while typing in a panel
    // =====================================================================

    public void EnableKeyboard()
    {
        if (_keyboard || _hwnd == 0) return;
        _keyboard = true;
        Win32.SetExStyle(_hwnd, 0, Win32.WS_EX_NOACTIVATE);
        Activate();
        Win32.SetForegroundWindow(_hwnd);
    }

    private void LeaveKeyboard()
    {
        if (!_keyboard || _hwnd == 0) return;
        _keyboard = false;
        Win32.SetExStyle(_hwnd, Win32.WS_EX_NOACTIVATE, 0);
    }

    private void SetClickThrough(bool on)
    {
        if (_hwnd == 0 || _clickThrough == on) return;
        _clickThrough = on;
        if (on) Win32.SetExStyle(_hwnd, Win32.WS_EX_TRANSPARENT, 0);
        else Win32.SetExStyle(_hwnd, 0, Win32.WS_EX_TRANSPARENT);
    }

    // =====================================================================
    // Animation: one spring step per rendered frame
    // =====================================================================

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = null;
        RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        var dt = _lastFrame is { } last ? (time - last).TotalSeconds : 1.0 / 60;
        _lastFrame = time;

        var moving = _w.Step(dt) | _h.Step(dt);
        ApplyFrame();

        if (moving) RequestAnimationFrame(OnFrame);
        else _animating = false;
    }

    private void ApplyFrame()
    {
        var (x, y, w, h) = IslandRectDip();
        Island.Width = w;
        Island.Height = h;

        var targetH = _mode switch { Mode.Expanded => _panel.CardHeight, Mode.Alert => AlertH, _ => PeekH };
        var big = Math.Max(targetH, h);
        // 0 when closed, 1 when fully open.
        var t = big <= PeekH + SwellExtraH + 0.5 ? 0 : Math.Clamp((h - PeekH) / (big - PeekH), 0, 1);
        var radius = h <= PeekH + SwellExtraH + 0.5 ? h / 2 : PeekH / 2 + (OpenRadius - PeekH / 2) * t;
        Island.CornerRadius = _shape == IslandShape.Floating
            ? new CornerRadius(radius)
            : new CornerRadius(0, 0, radius, radius);

        // The peek fades fast as the island grows; the card arrives late and settles into place.
        PeekHost.Opacity = Math.Clamp(1 - Math.Max(0, (h - PeekH - SwellExtraH)) / 30, 0, 1);
        var reveal = Math.Clamp((t - 0.5) / 0.5, 0, 1);
        if (_mode == Mode.Alert || (AlertCard.IsVisible && !Card.IsVisible))
        {
            AlertCard.Opacity = reveal;
            Card.Opacity = 0;
        }
        else
        {
            // Fully faded out: switch the card off so it (and its shadow effect) isn't drawn at all.
            Card.IsVisible = reveal > 0.001 || _mode == Mode.Expanded;
            Card.Opacity = reveal;
            _cardShift.Y = -8 * (1 - reveal);
            var scale = Math.Clamp(Math.Min(w / CardW, h / Math.Max(_panel.CardHeight, 1)), 0.5, 1);
            _cardScale.ScaleX = scale;
            _cardScale.ScaleY = scale;
        }

        var s = RenderScaling;
        _glass?.Update(x * s, y * s, w * s, h * s, radius * s, flatTop: _shape == IslandShape.Notch);
    }

    // =====================================================================
    // Peeks: what the closed island shows
    // =====================================================================

    private void UpdatePeek()
    {
        var now = DateTime.Now;
        var claude = _hub.Settings.ClaudeStatus ? _hub.Claude.Sessions : [];
        var waiting = claude.FirstOrDefault(c => c.State == ClaudeState.Waiting);
        var working = claude.FirstOrDefault(c => c.State == ClaudeState.Working);

        PeekKind kind;
        if (now < _transientUntil) kind = PeekKind.Osd;
        else if (_hub.MicUse.MicInUse) kind = PeekKind.Call;
        else if (_hub.Timer.Running) kind = PeekKind.Timer;
        else if (waiting is not null || working is not null) kind = PeekKind.Claude;
        else if (_hub.Media.IsPlaying && _hub.Media.HasMedia) kind = PeekKind.Music;
        else kind = PeekKind.Default;

        switch (kind)
        {
            case PeekKind.Default:
                _peekTime.Text = now.ToString("HH:mm");
                _peekDate.Text = now.ToString("ddd d");
                _peekW = 220;
                break;
            case PeekKind.Music:
                _peekArt.Source = _hub.Media.Art;
                _peekSong.Text = _hub.Media.Title;
                _peekPlatform.Source = _hub.Media.PlatformLogo;
                _peekPlatform.IsVisible = _hub.Media.PlatformLogo is not null;
                _peekW = 280;
                break;
            case PeekKind.Claude:
                var c = waiting ?? working!;
                _peekClaudeDot.Background = U.Brush(c.State == ClaudeState.Waiting ? U.Amber : U.Green);
                _peekClaudeTitle.Text = c.Project;
                _peekClaudeState.Text = c.State == ClaudeState.Waiting ? "needs you?" : claude.Count(s => s.State != ClaudeState.Done) > 1 ? $"{claude.Count(s => s.State != ClaudeState.Done)} working" : "working";
                _peekW = 320;
                break;
            case PeekKind.Call:
                _peekCallTime.Text = U.Clock(now - _hub.MicUse.Since);
                _peekCallApp.Text = _hub.MicUse.App;
                _peekCallMic.Text = _hub.Audio.MicMuted ? Glyph.MicOff : Glyph.Mic;
                _peekCallMic.Foreground = U.Brush(_hub.Audio.MicMuted ? U.Red : U.Primary);
                _peekW = 260;
                break;
            case PeekKind.Timer:
                _peekTimerText.Text = U.Clock(_hub.Timer.Left);
                _peekW = 220;
                break;
            case PeekKind.Osd:
                _peekW = 280;
                break;
        }

        if (kind != _peek)
        {
            _peek = kind;
            _peekDefault.IsVisible = kind == PeekKind.Default;
            _peekMusic.IsVisible = kind == PeekKind.Music;
            _peekClaude.IsVisible = kind == PeekKind.Claude;
            _peekCall.IsVisible = kind == PeekKind.Call;
            _peekOsd.IsVisible = kind == PeekKind.Osd;
            _peekTimer.IsVisible = kind == PeekKind.Timer;
        }

        if (_mode == Mode.Collapsed)
        {
            var target = _peekW + (_swollen ? SwellExtraW : 0);
            if (Math.Abs(_w.Target - target) > 0.5)
            {
                _w.Target = target;
                StartAnimation();
            }
        }
    }

    /// <summary>A short pop-up in the closed island: volume, brightness, mic, colour.</summary>
    private void ShowOsd(string glyph, string text, double? level, string? swatch = null, string glyphColor = U.Primary)
    {
        if (IsHidden) return;
        _peekOsdIcon.Text = glyph;
        _peekOsdIcon.Foreground = U.Brush(glyphColor);
        _peekOsdIcon.IsVisible = swatch is null;
        _peekOsdSwatch.IsVisible = swatch is not null;
        if (swatch is not null) _peekOsdSwatch.Background = U.Brush(swatch);
        _peekOsdText.Text = text;
        _peekOsdTrack.IsVisible = level is not null;
        if (level is { } l) _peekOsdFill.Width = Math.Clamp(l, 0, 1) * 150;
        _transientUntil = DateTime.Now.AddMilliseconds(1600);
        UpdatePeek();
    }

    internal void ShowMicState(bool muted) =>
        ShowOsd(muted ? Glyph.MicOff : Glyph.Mic, muted ? "Microphone muted" : "Microphone on", null, null, muted ? U.Red : U.Primary);

    /// <summary>Ctrl+Alt+C: copies the colour under the pointer as hex.</summary>
    internal void PickColour()
    {
        if (!Win32.GetCursorPos(out var p)) return;
        var dc = Win32.GetDC(0);
        var c = Win32.GetPixel(dc, p.X, p.Y);
        Win32.ReleaseDC(0, dc);
        var hex = $"#{c & 0xFF:X2}{(c >> 8) & 0xFF:X2}{(c >> 16) & 0xFF:X2}";
        _ = Clipboard?.SetTextAsync(hex);
        ShowOsd(Glyph.Color, $"{hex} copied", null, hex);
        _transientUntil = DateTime.Now.AddMilliseconds(2600);
    }

    // =====================================================================
    // Timers
    // =====================================================================

    private void Tick()
    {
        if (_mode == Mode.Expanded) _panel.OnTick();

        if (_peek is PeekKind.Call or PeekKind.Timer || (_peek == PeekKind.Osd && DateTime.Now >= _transientUntil))
            UpdatePeek();

        if (_peek == PeekKind.Music && _hub.Media.IsPlaying)
        {
            foreach (var bar in _peekBars.Children.OfType<Border>()) bar.Height = 4 + _random.Next(11);
        }

        if (_mode == Mode.Alert && DateTime.Now >= _alertUntil) DismissAlert();
    }

    // =====================================================================
    // Adaptive glass: clear over dark pages, a little darker over bright ones
    // =====================================================================

    /// <summary>
    /// Like iOS materials: the glass stays nearly clear over dark content and darkens just
    /// enough over bright content for white text to stay readable. Brightness is measured in
    /// thin strips around the island (never under it), once a second.
    /// </summary>
    private void AdaptTint()
    {
        if (_look != IslandLook.Glass || _glass is null || IsHidden || _hwnd == 0) return;
        var r = IslandRectPx(0);
        const int gap = 70, strip = 20;
        var lum = BackdropProbe.Brightness(
        [
            (r.X - gap - strip, r.Y, strip, Math.Max(r.Height, 34)),
            (r.Right + gap, r.Y, strip, Math.Max(r.Height, 34)),
            (r.X, r.Bottom + gap - 14, r.Width, strip),
        ]);
        var t = Math.Clamp((lum - 0.35) / 0.5, 0, 1);
        t = t * t * (3 - 2 * t); // smoothstep
        var target = 0.13 + 0.37 * t;
        // Only repaint when the change is visible; an unchanged tint costs nothing.
        if (Math.Abs(target - _tintLevel) < 0.015) return;
        SetTint(_tintLevel + (target - _tintLevel) * 0.6);
        var frost = (byte)(255 * 0.07 * (1 - t));
        if (Frost.Background is not SolidColorBrush fb || Math.Abs(fb.Color.A - frost) > 2)
            Frost.Background = new SolidColorBrush(Color.FromArgb(frost, 255, 255, 255));
        if (Log.On) Log.Write($"glass: backdrop brightness {lum:F2}, tint {_tintLevel:F2}");
    }

    private void SetTint(double level)
    {
        _tintLevel = Math.Clamp(level, 0.1, 0.6);
        _tintTop.Color = Color.FromArgb((byte)(255 * _tintLevel * 0.85), 0x26, 0x26, 0x30);
        _tintBottom.Color = Color.FromArgb((byte)(255 * Math.Min(_tintLevel * 1.3, 0.7)), 0x12, 0x12, 0x18);
    }

    private void EverySecond()
    {
        AdaptTint();
        if (_peek == PeekKind.Default) UpdatePeek();
        _hub.Reminders.Tick();
        _hub.Timer.Tick();
        CheckScreen();
    }

    // =====================================================================
    // Screen: step aside for fullscreen apps, stay on top
    // =====================================================================

    private bool IsHidden => _hiddenByUser || (_hiddenForFullscreen && _mode != Mode.Alert);

    private void RefreshVisibility()
    {
        var visible = !IsHidden;
        Root.IsVisible = visible;
        _glass?.SetVisible(visible && _look == IslandLook.Glass);
        if (!visible) SetClickThrough(true);
    }

    private void CheckScreen()
    {
        if (_hwnd == 0) return;

        var fullscreen = ForegroundIsFullscreenHere();
        if (fullscreen != _hiddenForFullscreen)
        {
            Log.Write($"fullscreen {fullscreen} fg class {Win32.ClassNameOf(Win32.GetForegroundWindow())}");
            _hiddenForFullscreen = fullscreen;
            if (fullscreen && _mode == Mode.Expanded) Collapse();
            RefreshVisibility();
        }

        // Some apps claim the top layer; take it back without stealing focus.
        if (!IsHidden && !_keyboard)
        {
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
            _glass?.StackBelow(_hwnd);
        }
    }

    private bool ForegroundIsFullscreenHere()
    {
        var fg = Win32.GetForegroundWindow();
        if (fg == 0 || fg == _hwnd) return _hiddenForFullscreen;

        var cls = Win32.ClassNameOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "CinyNotch.Glass") return false;

        var fgMonitor = Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST);
        if (fgMonitor != Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST)) return false;

        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(fgMonitor, ref info) || !Win32.GetWindowRect(fg, out var r)) return false;

        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}
