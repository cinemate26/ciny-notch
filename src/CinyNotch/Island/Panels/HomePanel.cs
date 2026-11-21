using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CinyNotch.Services;
using CinyNotch.Ui;
using Windows.System.Power;
using U = CinyNotch.Ui.Ui;

namespace CinyNotch.Island.Panels;

/// <summary>
/// Home: the time, what's playing (or the weather), the apps that are open, a quick timer
/// and the battery.
/// </summary>
internal sealed class HomePanel : IslandPanel
{
    private readonly TextBlock _time = U.Text("", 46);
    private readonly TextBlock _date = U.Text("", 13, U.Secondary);

    // Right of the clock: a mini player while something is playing, otherwise the weather.
    private readonly Panel _right = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _player = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Width = 300 };
    private readonly Image _art = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _artIcon = U.Icon(Glyph.Music, 18, U.Muted);
    private readonly Image _platform = new() { Width = 14, Height = 14 };
    private readonly Border _platformBadge = new()
    {
        Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = U.Brush("#E6141418"),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, -5, -5),
    };
    private readonly TextBlock _songTitle = U.Text("", 13, U.Primary, FontWeight.Regular);
    private readonly TextBlock _songArtist = U.Text("", 12, U.Secondary);
    private readonly Border _progressTrack = new() { Height = 3, CornerRadius = new CornerRadius(1.5), Background = U.Brush("#33FFFFFF"), Margin = new Thickness(0, 6, 0, 4) };
    private readonly Border _progressFill = new() { Height = 3, CornerRadius = new CornerRadius(1.5), Background = U.Brush("#F2F2F4"), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _play;
    private readonly StackPanel _weatherBig;
    private readonly TextBlock _weatherIcon = U.Icon(Glyph.Cloud, 20);
    private readonly TextBlock _temp = U.Text("", 26);
    private readonly TextBlock _weatherLine = U.Text("", 12, U.Secondary);
    private readonly TextBlock _weatherPlace = U.Text("", 12, U.Muted);

    // Open apps.
    private readonly StackPanel _apps = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private string _appsKey = "";
    private int _ticks;

    // Bottom row.
    private readonly StackPanel _timerRow = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _smallWeather = U.Text("", 12, U.Secondary);
    private readonly TextBlock _battery = U.Text("", 12, U.Secondary);

    public HomePanel(Hub hub, IIslandHost host) : base(hub, host)
    {
        _date.Margin = new Thickness(2, -2, 0, 0);
        var clock = U.Column(0, _time, _date);
        clock.VerticalAlignment = VerticalAlignment.Center;

        // Mini player.
        var artFrame = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = U.Brush("#1FFFFFFF"),
            Child = new Panel { Children = { _artIcon, _art } }, Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(artFrame, "Open Music");
        artFrame.Tapped += (_, _) => Host.SelectTab("Music");
        _platformBadge.Child = _platform;
        var artWithBadge = new Panel { Width = 48, Height = 48, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Children = { artFrame, _platformBadge } };

        _play = U.Round(Glyph.Play, () => _ = Hub.Media.PlayPauseAsync(), 28, 11);
        var controls = U.Row(2,
            U.Round(Glyph.Previous, () => _ = Hub.Media.PreviousAsync(), 26, 10, flat: true),
            _play,
            U.Round(Glyph.Next, () => _ = Hub.Media.NextAsync(), 26, 10, flat: true));
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        titleRow.Children.Add(U.Column(0, _songTitle, _songArtist));
        Grid.SetColumn(controls, 1);
        controls.VerticalAlignment = VerticalAlignment.Center;
        controls.Margin = new Thickness(6, 0, 0, 0);
        titleRow.Children.Add(controls);
        _progressTrack.Child = _progressFill;
        var playerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { titleRow, _progressTrack } };
        _player.Children.Add(artWithBadge);
        Grid.SetColumn(playerText, 1);
        _player.Children.Add(playerText);

        // Big weather, shown when nothing plays.
        _weatherLine.TextAlignment = TextAlignment.Right;
        _weatherPlace.TextAlignment = TextAlignment.Right;
        var weatherTop = U.Row(8, _weatherIcon, _temp);
        weatherTop.HorizontalAlignment = HorizontalAlignment.Right;
        _weatherBig = U.Column(2, weatherTop, _weatherLine, _weatherPlace);
        _weatherBig.HorizontalAlignment = HorizontalAlignment.Right;
        _weatherBig.MaxWidth = 260;

        _right.Children.Add(_player);
        _right.Children.Add(_weatherBig);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(clock);
        Grid.SetColumn(_right, 1);
        top.Children.Add(_right);

        _apps.Margin = new Thickness(-4, 12, 0, 0);

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        bottom.Children.Add(_timerRow);
        var status = U.Row(14, _smallWeather, _battery);
        status.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(status, 1);
        bottom.Children.Add(status);

        Content = new StackPanel { Margin = new Thickness(26, 6, 26, 14), Children = { top, _apps, bottom } };

        hub.Weather.Changed += RefreshWeather;
        hub.Timer.Changed += RefreshTimer;
        hub.Media.Changed += RefreshPlayer;
        RefreshWeather();
        RefreshTimer();
        RefreshPlayer();
        OnTick();
    }

    public override string TabName => "Home";
    public override double CardHeight => 248;

    public override void OnShown()
    {
        RefreshBattery();
        RefreshPlayer();
        RefreshApps();
        OnTick();
    }

    public override void OnTick()
    {
        var now = DateTime.Now;
        _time.Text = now.ToString("HH:mm");
        _date.Text = now.ToString("dddd, d MMMM");
        if (Hub.Timer.Running && _timerRow.Children.Count > 1 && _timerRow.Children[1] is TextBlock left)
            left.Text = U.Clock(Hub.Timer.Left);

        var m = Hub.Media;
        if (m.HasMedia && m.Duration > TimeSpan.Zero)
            _progressFill.Width = Math.Clamp(m.Position.TotalSeconds / m.Duration.TotalSeconds, 0, 1) * Math.Max(_progressTrack.Bounds.Width, 1);

        // Open apps change less often than the clock: look every two seconds.
        if (++_ticks % 8 == 0) RefreshApps();
    }

    private void RefreshPlayer()
    {
        var m = Hub.Media;
        _player.IsVisible = m.HasMedia;
        _weatherBig.IsVisible = !m.HasMedia;
        RefreshWeather();
        if (!m.HasMedia) return;

        _songTitle.Text = m.Title;
        _songArtist.Text = string.Join("  ·  ", new[] { m.Artist, m.Platform }.Where(s => !string.IsNullOrWhiteSpace(s)));
        _art.Source = m.Art;
        _artIcon.IsVisible = m.Art is null;
        _platform.Source = m.PlatformLogo;
        _platformBadge.IsVisible = m.PlatformLogo is not null;
        U.SetGlyph(_play, m.IsPlaying ? Glyph.Pause : Glyph.Play);
    }

    private void RefreshApps()
    {
        var apps = OpenApps.List(Environment.ProcessId);
        var key = string.Join("|", apps.Select(a => a.Key + (a.Active ? "*" : "")));
        if (key == _appsKey) return;
        _appsKey = key;

        _apps.Children.Clear();
        foreach (var app in apps) _apps.Children.Add(AppTile(app));
        _apps.IsVisible = apps.Count > 0;
    }

    private Control AppTile(OpenApp app)
    {
        Control icon = app.Icon is not null
            ? new Image { Source = app.Icon, Width = 24, Height = 24 }
            : U.Text(app.Name.Length > 0 ? app.Name[..1].ToUpperInvariant() : "?", 14, U.Primary, FontWeight.Regular);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;

        // A small dot under the app you're in right now.
        var dot = new Border
        {
            Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = U.Brush("#F2F2F4"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 1), IsVisible = app.Active,
        };

        var tile = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Panel { Children = { icon, dot } },
        };
        ToolTip.SetTip(tile, app.Name);
        tile.PointerEntered += (_, _) => tile.Background = U.Brush("#1FFFFFFF");
        tile.PointerExited += (_, _) => tile.Background = Brushes.Transparent;
        tile.Tapped += (_, _) =>
        {
            Host.Close();
            OpenApps.Activate(app.Window);
        };
        return tile;
    }

    private void RefreshWeather()
    {
        var w = Hub.Weather.Now;
        if (w is null)
        {
            _weatherIcon.IsVisible = false;
            _temp.Text = "";
            _weatherLine.Text = Hub.Weather.Status;
            _weatherLine.TextWrapping = TextWrapping.Wrap;
            _weatherPlace.Text = "";
            _smallWeather.Text = "";
            return;
        }
        var (text, glyph) = WeatherService.Describe(w.Code);
        _weatherIcon.IsVisible = true;
        _weatherIcon.Text = glyph;
        _temp.Text = $"{Math.Round(w.Temp)}°";
        _weatherLine.TextWrapping = TextWrapping.NoWrap;
        _weatherLine.Text = $"{text}  ·  H {Math.Round(w.High)}°  L {Math.Round(w.Low)}°";
        _weatherPlace.Text = w.Place;
        // With music on top, the weather moves to the bottom row in short form.
        _smallWeather.Text = Hub.Media.HasMedia ? $"{Math.Round(w.Temp)}°  {text}" : "";
    }

    private void RefreshTimer()
    {
        _timerRow.Children.Clear();
        if (Hub.Timer.Running)
        {
            _timerRow.Children.Add(U.Icon(Glyph.Timer, 13, U.Secondary));
            _timerRow.Children.Add(U.Text(U.Clock(Hub.Timer.Left), 14));
            _timerRow.Children.Add(U.Chip("Cancel", Hub.Timer.Cancel));
            return;
        }
        _timerRow.Children.Add(U.Icon(Glyph.Timer, 13, U.Muted));
        foreach (var minutes in new[] { 5, 10, 25 })
            _timerRow.Children.Add(U.Chip($"{minutes} min", () => Hub.Timer.Start(TimeSpan.FromMinutes(minutes))));
    }

    private void RefreshBattery()
    {
        try
        {
            if (PowerManager.BatteryStatus == BatteryStatus.NotPresent) { _battery.Text = ""; return; }
            var plugged = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
            _battery.Text = $"Battery {PowerManager.RemainingChargePercent}%{(plugged ? ", plugged in" : "")}";
        }
        catch
        {
            _battery.Text = "";
        }
    }
}
