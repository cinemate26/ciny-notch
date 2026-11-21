using CinyNotch.Settings;

namespace CinyNotch.Services;

/// <summary>Every feature's service, created once and shared by the island and its panels.</summary>
internal sealed class Hub
{
    public required AppSettings Settings { get; init; }
    public AudioService Audio { get; } = new();
    public MicUseWatcher MicUse { get; } = new();
    public MediaService Media { get; } = new();
    public LyricsService Lyrics { get; } = new();
    public ClaudeWatcher Claude { get; } = new();
    public ClipboardHistory Clipboard { get; } = new();
    public Reminders Reminders { get; } = new();
    public BrightnessService Brightness { get; } = new();
    public RadioService Radios { get; } = new();
    public WeatherService Weather { get; } = new();
    public CountdownTimer Timer { get; } = new();

    /// <summary>The mic shortcut that could be registered (another app may own Ctrl+Alt+M).</summary>
    public string MicShortcut { get; set; } = "Ctrl+Alt+M";

    public void ApplySettings()
    {
        Lyrics.Enabled = Settings.Lyrics;
        Claude.Enabled = Settings.ClaudeStatus;
        Weather.SetCity(Settings.WeatherCity);
    }
}
