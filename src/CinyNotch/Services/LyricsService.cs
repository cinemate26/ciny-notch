using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using CinyNotch.Diagnostics;

namespace CinyNotch.Services;

internal sealed record LyricLine(TimeSpan At, string Text);

/// <summary>
/// Synced lyrics from LRCLIB (lrclib.net), a free public lyrics library with no account or key.
/// Only the song's title, artist, album and length are sent. Results are kept in memory.
/// </summary>
internal sealed partial class LyricsService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://lrclib.net/"),
        Timeout = TimeSpan.FromSeconds(8),
        DefaultRequestHeaders = { { "User-Agent", "CinyNotch/0.1 (desktop notch app)" } },
    };

    private readonly Dictionary<string, IReadOnlyList<LyricLine>> _cache = new();

    public bool Enabled { get; set; } = true;

    public async Task<IReadOnlyList<LyricLine>> GetAsync(string title, string artist, string album, TimeSpan duration)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(title)) return [];
        var key = $"{title}|{artist}";
        if (_cache.TryGetValue(key, out var hit)) return hit;

        IReadOnlyList<LyricLine> lines = [];
        try
        {
            var q = $"api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (!string.IsNullOrWhiteSpace(album)) q += $"&album_name={Uri.EscapeDataString(album)}";
            if (duration > TimeSpan.Zero) q += $"&duration={(int)duration.TotalSeconds}";
            lines = Parse(await SyncedFromGet(q));

            if (lines.Count == 0)
            {
                var search = $"api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                lines = Parse(await SyncedFromSearch(search));
            }
        }
        catch (Exception ex)
        {
            Log.Write($"lyrics: {ex.Message}");
        }
        _cache[key] = lines;
        return lines;
    }

    private static async Task<string?> SyncedFromGet(string query)
    {
        using var res = await Http.GetAsync(query);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
    }

    private static async Task<string?> SyncedFromSearch(string query)
    {
        using var res = await Http.GetAsync(query);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String) return s.GetString();
        }
        return null;
    }

    private static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return [];
        var list = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var m = LineRegex().Match(raw);
            if (!m.Success) continue;
            var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var seconds = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var text = m.Groups[3].Value.Trim();
            list.Add(new LyricLine(TimeSpan.FromSeconds(minutes * 60 + seconds), text));
        }
        list.Sort((a, b) => a.At.CompareTo(b.At));
        return list;
    }

    /// <summary>Index of the line being sung at <paramref name="position"/>, or -1 before the first.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        var index = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].At <= position) index = i;
            else break;
        }
        return index;
    }

    [GeneratedRegex(@"^\s*\[(\d+):(\d+(?:\.\d+)?)\](.*)$")]
    private static partial Regex LineRegex();
}
