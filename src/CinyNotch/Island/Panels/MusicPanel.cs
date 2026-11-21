using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CinyNotch.Services;
using CinyNotch.Ui;
using U = CinyNotch.Ui.Ui;

namespace CinyNotch.Island.Panels;

/// <summary>Music: what's playing, controls, a scrub bar and synced lyrics.</summary>
internal sealed class MusicPanel : IslandPanel
{
    private readonly Border _art = new()
    {
        Width = 104,
        Height = 104,
        CornerRadius = new CornerRadius(14),
        Background = U.Brush("#1FFFFFFF"),
        ClipToBounds = true,
    };
    private readonly Image _artImage = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _artIcon = U.Icon(Glyph.Music, 30, U.Muted);
    // The platform's logo (YouTube, Spotify...) sits on the corner of the artwork.
    private readonly Image _logo = new() { Width = 22, Height = 22, Stretch = Stretch.Uniform };
    private readonly Border _logoBadge = new()
    {
        Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
        Background = U.Brush("#E6141418"),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, -8, -8),
    };
    private readonly TextBlock _title = U.Text("", 17, U.Primary, FontWeight.Regular);
    private readonly TextBlock _artist = U.Text("", 13, U.Secondary);
    private readonly Slider _scrub = new() { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _elapsed = U.Text("", 11, U.Muted);
    private readonly TextBlock _remaining = U.Text("", 11, U.Muted);
    private readonly Button _play;
    private readonly TextBlock _lyricNow = U.Text("", 13, U.Primary);
    private readonly TextBlock _lyricNext = U.Text("", 12, U.Muted);
    private readonly StackPanel _lyrics;
    private readonly Control _player;
    private readonly Control _empty;

    private IReadOnlyList<LyricLine> _lines = [];
    private bool _dragging;
    private string _lyricsFor = "";

    public MusicPanel(Hub hub, IIslandHost host) : base(hub, host)
    {
        _art.Child = new Panel { Children = { _artIcon, _artImage } };
        _logoBadge.Child = _logo;
        var artWithLogo = new Panel { Width = 104, Height = 104, VerticalAlignment = VerticalAlignment.Top, Children = { _art, _logoBadge } };

        _play = U.Round(Glyph.Play, () => _ = Hub.Media.PlayPauseAsync(), 40, 16);
        var prev = U.Round(Glyph.Previous, () => _ = Hub.Media.PreviousAsync(), 34, 13, flat: true);
        var next = U.Round(Glyph.Next, () => _ = Hub.Media.NextAsync(), 34, 13, flat: true);
        var controls = U.Row(6, prev, _play, next);

        _scrub.AddHandler(PointerPressedEvent, (_, _) => _dragging = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _scrub.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            _dragging = false;
            if (Hub.Media.Duration > TimeSpan.Zero) _ = Hub.Media.SeekAsync(Hub.Media.Duration * _scrub.Value);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var times = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        times.Children.Add(_elapsed);
        Grid.SetColumn(_remaining, 1);
        times.Children.Add(_remaining);

        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        titleRow.Children.Add(U.Column(1, _title, _artist));
        Grid.SetColumn(controls, 1);
        controls.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(controls);

        var right = new StackPanel { Margin = new Thickness(18, 2, 0, 0), Children = { titleRow, _scrub, times } };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        top.Children.Add(artWithLogo);
        Grid.SetColumn(right, 1);
        top.Children.Add(right);

        _lyrics = U.Column(1, _lyricNow, _lyricNext);
        _lyrics.Margin = new Thickness(0, 12, 0, 0);

        _player = new StackPanel { Children = { top, _lyrics } };
        _empty = U.Column(4,
            U.Text("Nothing playing", 24),
            U.Text("Play something in Spotify, a browser or any music app and it shows up here.", 13, U.Secondary, trim: false));
        ((TextBlock)((StackPanel)_empty).Children[1]).TextWrapping = TextWrapping.Wrap;
        _empty.VerticalAlignment = VerticalAlignment.Center;

        Content = new Panel { Margin = new Thickness(24, 6, 24, 14), Children = { _player, _empty } };

        hub.Media.Changed += Refresh;
        hub.Media.TrackChanged += () => _ = LoadLyricsAsync();
        Refresh();
    }

    public override string TabName => "Music";
    public override double CardHeight => 224;

    public override void OnShown()
    {
        Refresh();
        _ = LoadLyricsAsync();
    }

    public override void OnTick()
    {
        var m = Hub.Media;
        if (!m.HasMedia) return;
        var pos = m.Position;
        if (!_dragging && m.Duration > TimeSpan.Zero) _scrub.Value = pos.TotalSeconds / m.Duration.TotalSeconds;
        _elapsed.Text = U.Clock(pos);
        _remaining.Text = m.Duration > TimeSpan.Zero ? "-" + U.Clock(m.Duration - pos) : "";

        if (_lines.Count > 0)
        {
            var i = LyricsService.IndexAt(_lines, pos + TimeSpan.FromMilliseconds(250));
            _lyricNow.Text = i >= 0 ? _lines[i].Text : "";
            _lyricNext.Text = i + 1 < _lines.Count ? _lines[i + 1].Text : "";
        }
    }

    private void Refresh()
    {
        var m = Hub.Media;
        _player.IsVisible = m.HasMedia;
        _empty.IsVisible = !m.HasMedia;
        if (!m.HasMedia) return;

        _title.Text = m.Title;
        var parts = new[] { m.Artist, m.Album, m.Platform }.Where(s => !string.IsNullOrWhiteSpace(s));
        _artist.Text = string.Join("  ·  ", parts);
        _artImage.Source = m.Art;
        _logo.Source = m.PlatformLogo;
        _logoBadge.IsVisible = m.PlatformLogo is not null;
        _artIcon.IsVisible = m.Art is null;
        U.SetGlyph(_play, m.IsPlaying ? Glyph.Pause : Glyph.Play);
        _scrub.IsEnabled = m.CanSeek;
        OnTick();
    }

    private async Task LoadLyricsAsync()
    {
        var m = Hub.Media;
        var key = $"{m.Title}|{m.Artist}";
        if (key == _lyricsFor) return;
        _lyricsFor = key;
        _lines = [];
        _lyricNow.Text = Hub.Settings.Lyrics && m.HasMedia ? "Looking for lyrics..." : "";
        _lyricNext.Text = "";
        if (!m.HasMedia || !Hub.Settings.Lyrics) return;

        var lines = await Hub.Lyrics.GetAsync(m.Title, m.Artist, m.Album, m.Duration);
        if (key != $"{Hub.Media.Title}|{Hub.Media.Artist}") return;
        _lines = lines;
        if (lines.Count == 0) _lyricNow.Text = "No synced lyrics for this one.";
        OnTick();
    }
}
