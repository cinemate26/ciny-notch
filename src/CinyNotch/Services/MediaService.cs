using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CinyNotch.Diagnostics;
using Windows.Media.Control;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace CinyNotch.Services;

/// <summary>
/// What's playing, from any app that reports media to Windows (Spotify, browsers, Apple Music,
/// VLC...). Same source as the media flyout in Windows' volume panel.
/// </summary>
internal sealed class MediaService
{
    private SessionManager? _manager;
    private Session? _session;
    private TimeSpan _position;
    private DateTimeOffset _positionStamp;
    private string _artKey = "";

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public string AppId { get; private set; } = "";
    public Bitmap? Art { get; private set; }
    /// <summary>Where it's playing: YouTube, Spotify, Chrome... (empty if unknown).</summary>
    public string Platform { get; private set; } = "";
    public Bitmap? PlatformLogo { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool HasMedia => _session is not null && Title.Length > 0;
    public TimeSpan Duration { get; private set; }
    public bool CanSeek { get; private set; }

    public TimeSpan Position
    {
        get
        {
            var p = IsPlaying ? _position + (DateTimeOffset.Now - _positionStamp) : _position;
            if (p < TimeSpan.Zero) p = TimeSpan.Zero;
            return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
        }
    }

    public event Action? Changed;
    public event Action? TrackChanged;

    public async Task StartAsync()
    {
        try
        {
            _manager = await SessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Post(() => Attach(PickSession()));
            _manager.SessionsChanged += (_, _) => Post(() => Attach(PickSession()));
            Attach(PickSession());
        }
        catch (Exception ex)
        {
            Log.Write($"media unavailable: {ex.Message}");
        }
    }

    /// <summary>Prefers a session that is actually playing over the system's current pick.</summary>
    private Session? PickSession()
    {
        if (_manager is null) return null;
        try
        {
            foreach (var s in _manager.GetSessions())
            {
                if (s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return s;
            }
            return _manager.GetCurrentSession();
        }
        catch
        {
            return null;
        }
    }

    private void Attach(Session? session)
    {
        if (ReferenceEquals(session, _session)) return;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }
        _session = session;
        if (_session is null)
        {
            Title = Artist = Album = AppId = Platform = "";
            Art = null;
            PlatformLogo = null;
            _artKey = "";
            IsPlaying = false;
            Changed?.Invoke();
            TrackChanged?.Invoke();
            return;
        }
        AppId = _session.SourceAppUserModelId ?? "";
        _artKey = ""; // a new app: re-read art and platform even for the same title
        _session.MediaPropertiesChanged += OnProps;
        _session.PlaybackInfoChanged += OnPlayback;
        _session.TimelinePropertiesChanged += OnTimeline;
        _ = RefreshPropsAsync();
        RefreshPlayback();
        RefreshTimeline();
    }

    private void OnProps(Session s, MediaPropertiesChangedEventArgs e) => Post(() => _ = RefreshPropsAsync());
    private void OnPlayback(Session s, PlaybackInfoChangedEventArgs e) => Post(() =>
    {
        RefreshPlayback();
        // A paused session may have handed over to one that is playing.
        if (!IsPlaying) Attach(PickSession());
    });
    private void OnTimeline(Session s, TimelinePropertiesChangedEventArgs e) => Post(RefreshTimeline);

    private async Task RefreshPropsAsync()
    {
        var session = _session;
        if (session is null) return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(session, _session) || props is null) return;
            var trackChanged = props.Title != Title || props.Artist != Artist;
            Title = props.Title ?? "";
            Artist = string.IsNullOrEmpty(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;
            Album = props.AlbumTitle ?? "";

            var key = $"{Title}|{Artist}|{Album}";
            if (key != _artKey)
            {
                _artKey = key;
                Art = props.Thumbnail is null ? null : await LoadArtAsync(props.Thumbnail);
                (Platform, PlatformLogo) = DetectPlatform(AppId, Title);
            }
            Changed?.Invoke();
            if (trackChanged) TrackChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Write($"media props: {ex.Message}");
        }
    }

    private static async Task<Bitmap?> LoadArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var source = stream.AsStreamForRead();
            var copy = new MemoryStream();
            await source.CopyToAsync(copy);
            copy.Position = 0;
            return Bitmap.DecodeToWidth(copy, 192);
        }
        catch
        {
            return null;
        }
    }

    private void RefreshPlayback()
    {
        try
        {
            var info = _session?.GetPlaybackInfo();
            var playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (playing != IsPlaying)
            {
                // Freeze or restart the running clock at the moment of change.
                _position = Position;
                _positionStamp = DateTimeOffset.Now;
            }
            IsPlaying = playing;
            CanSeek = info?.Controls.IsPlaybackPositionEnabled ?? false;
            Changed?.Invoke();
        }
        catch
        {
            // The app went away mid-call; the next session change will tidy up.
        }
    }

    private void RefreshTimeline()
    {
        try
        {
            var t = _session?.GetTimelineProperties();
            if (t is null) return;
            Duration = t.EndTime - t.StartTime;
            _position = t.Position;
            _positionStamp = t.LastUpdatedTime == default ? DateTimeOffset.Now : t.LastUpdatedTime;
            if (_positionStamp > DateTimeOffset.Now) _positionStamp = DateTimeOffset.Now;
            Changed?.Invoke();
        }
        catch
        {
            // Some players don't publish a timeline.
        }
    }

    public async Task PlayPauseAsync()
    {
        if (_session is not null) await _session.TryTogglePlayPauseAsync();
    }

    public async Task NextAsync()
    {
        if (_session is not null) await _session.TrySkipNextAsync();
    }

    public async Task PreviousAsync()
    {
        if (_session is not null) await _session.TrySkipPreviousAsync();
    }

    public async Task SeekAsync(TimeSpan to)
    {
        if (_session is null || !CanSeek) return;
        _position = to;
        _positionStamp = DateTimeOffset.Now;
        await _session.TryChangePlaybackPositionAsync(to.Ticks);
    }

    private static readonly (string Id, string Process, string Name)[] Browsers =
    [
        ("chrome", "chrome", "Chrome"),
        ("msedge", "msedge", "Edge"),
        ("308046b0af4a39cb", "firefox", "Firefox"),
        ("firefox", "firefox", "Firefox"),
        ("brave", "brave", "Brave"),
        ("opera", "opera", "Opera"),
        ("vivaldi", "vivaldi", "Vivaldi"),
    ];

    /// <summary>
    /// Works out the platform from the app that reports the media. Browsers report only
    /// "Chrome" or "Edge", so their window titles say which site it is (YouTube, Spotify...).
    /// </summary>
    private static (string Name, Bitmap? Logo) DetectPlatform(string appId, string title)
    {
        var id = appId.ToLowerInvariant();
        try
        {
            if (id.Contains("spotify")) return ("Spotify", AppIcons.ForProcess("Spotify") ?? AppIcons.SpotifyMark);
            if (id.Contains("applemusic")) return ("Apple Music", AppIcons.ForProcess("AppleMusic"));
            if (id.Contains("zunemusic")) return ("Media Player", null);
            if (id.Contains("vlc")) return ("VLC", AppIcons.ForProcess("vlc"));

            foreach (var b in Browsers)
            {
                if (!id.Contains(b.Id)) continue;
                var titles = AppIcons.WindowTitles(b.Process);
                // The tab playing this track, if it is the active tab; otherwise any tab of the browser.
                var probe = title.Length > 12 ? title[..12] : title;
                var tab = titles.FirstOrDefault(t => probe.Length > 0 && t.Contains(probe, StringComparison.OrdinalIgnoreCase));
                var text = tab ?? string.Join(" | ", titles);
                if (text.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase)) return ("YouTube Music", AppIcons.YouTubeMusic);
                if (text.Contains("YouTube", StringComparison.OrdinalIgnoreCase)) return ("YouTube", AppIcons.YouTube);
                if (text.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return ("Spotify", AppIcons.SpotifyMark);
                if (text.Contains("SoundCloud", StringComparison.OrdinalIgnoreCase)) return ("SoundCloud", AppIcons.ForProcess(b.Process));
                return (b.Name, AppIcons.ForProcess(b.Process));
            }

            var exe = Path.GetFileNameWithoutExtension(appId.Split('!')[0]);
            return ("", AppIcons.ForProcess(exe));
        }
        catch (Exception ex)
        {
            Log.Write($"platform: {ex.Message}");
            return ("", null);
        }
    }

    private static void Post(Action a) => Dispatcher.UIThread.Post(a);
}
