using System.Text.Json;
using System.Text.Json.Serialization;

namespace CinyNotch.Settings;

public enum IslandShape
{
    Notch,     // hangs from the top edge, flat top, round bottom corners
    Floating,  // a pill that floats just under the top edge
}

public enum IslandLook
{
    Glass,     // real blur of what's behind
    Dark,      // solid dark grey
    Black,     // pure black, for OLED screens
}

public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandShape Shape { get; set; } = IslandShape.Notch;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandLook Look { get; set; } = IslandLook.Glass;

    public bool Lyrics { get; set; } = true;
    public bool ClaudeStatus { get; set; } = true;
    public bool ClaudeAlerts { get; set; } = true;
    public string WeatherCity { get; set; } = "";

    /// <summary>Start with Windows is switched on once, the first time the app runs.</summary>
    public bool AutostartSetUp { get; set; }

    private static readonly string Folder = CinyNotch.Services.Paths.Data;

    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public event Action? Changed;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch
        {
            // A broken settings file falls back to defaults rather than stopping the island.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // Settings are a convenience; a failed write keeps the current session's choice.
        }
        Changed?.Invoke();
    }
}
