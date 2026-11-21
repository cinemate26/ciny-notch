using System.Text.Json;

namespace CinyNotch.Services;

/// <summary>Everything the island keeps lives in the project's "data" folder, on this PC only.</summary>
internal static class Store
{
    public static readonly string Folder = Paths.Data;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PathOf(string name)
    {
        Directory.CreateDirectory(Folder);
        return Path.Combine(Folder, name);
    }

    public static T Load<T>(string name) where T : new()
    {
        try
        {
            var path = PathOf(name);
            if (File.Exists(path)) return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? new T();
        }
        catch
        {
            // A damaged file starts fresh rather than stopping the island.
        }
        return new T();
    }

    public static void Save<T>(string name, T value)
    {
        try
        {
            var path = PathOf(name);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // Saving is best effort; the in-memory state stays correct for this session.
        }
    }
}
