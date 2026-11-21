using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Avalonia.Threading;
using CinyNotch.Diagnostics;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace CinyNotch.Services;

/// <summary>Wi-Fi and Bluetooth on/off, through Windows' own radio switches.</summary>
internal sealed class RadioService
{
    private Radio? _wifi;
    private Radio? _bluetooth;

    public bool HasWifi => _wifi is not null;
    public bool HasBluetooth => _bluetooth is not null;
    public bool WifiOn => _wifi?.State == RadioState.On;
    public bool BluetoothOn => _bluetooth?.State == RadioState.On;

    public event Action? Changed;

    public async Task StartAsync()
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) { Log.Write($"radios: {access}"); return; }
            foreach (var r in await Radio.GetRadiosAsync())
            {
                if (r.Kind == RadioKind.WiFi && _wifi is null) _wifi = r;
                if (r.Kind == RadioKind.Bluetooth && _bluetooth is null) _bluetooth = r;
                r.StateChanged += (_, _) => Dispatcher.UIThread.Post(() => Changed?.Invoke());
            }
            Log.Write($"radios: wifi {(_wifi is null ? "none" : _wifi.State.ToString())}, bluetooth {(_bluetooth is null ? "none" : _bluetooth.State.ToString())}");
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Write($"radios unavailable: {ex.Message}");
        }
    }

    public async Task ToggleWifiAsync()
    {
        if (_wifi is not null) await _wifi.SetStateAsync(WifiOn ? RadioState.Off : RadioState.On);
    }

    public async Task ToggleBluetoothAsync()
    {
        if (_bluetooth is not null) await _bluetooth.SetStateAsync(BluetoothOn ? RadioState.Off : RadioState.On);
    }
}

internal sealed record WeatherNow(double Temp, double High, double Low, int Code, bool IsDay, string Place);

/// <summary>
/// Weather from Open-Meteo (open-meteo.com): free, no account or key. Only the city name you
/// type in Settings (and then its coordinates) is sent. Refreshes every 30 minutes.
/// </summary>
internal sealed class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _timer;
    private string _city = "";
    private (double Lat, double Lon, string Name)? _place;

    public WeatherNow? Now { get; private set; }
    public string Status { get; private set; } = "Set your city in Settings to see the weather.";
    public event Action? Changed;

    public WeatherService()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(30), DispatcherPriority.Background, (_, _) => _ = RefreshAsync());
    }

    public void SetCity(string city)
    {
        city = city.Trim();
        if (city == _city) return;
        _city = city;
        _place = null;
        Now = null;
        Status = city.Length == 0 ? "Set your city in Settings to see the weather." : "Checking the weather...";
        Changed?.Invoke();
        _ = RefreshAsync();
        _timer.Start();
    }

    public async Task RefreshAsync()
    {
        if (_city.Length == 0) return;
        try
        {
            if (_place is null)
            {
                var geo = await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?count=1&name={Uri.EscapeDataString(_city)}");
                using var g = JsonDocument.Parse(geo);
                if (!g.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                {
                    Status = $"Couldn't find \"{_city}\". Check the city in Settings.";
                    Changed?.Invoke();
                    return;
                }
                var first = results[0];
                _place = (first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble(), first.GetProperty("name").GetString() ?? _city);
            }

            var (lat, lon, name) = _place.Value;
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=auto");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");
            var daily = doc.RootElement.GetProperty("daily");
            Now = new WeatherNow(
                cur.GetProperty("temperature_2m").GetDouble(),
                daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                cur.GetProperty("weather_code").GetInt32(),
                cur.GetProperty("is_day").GetInt32() == 1,
                name);
            Status = "";
        }
        catch (Exception ex)
        {
            Log.Write($"weather: {ex.Message}");
            if (Now is null) Status = "Weather is unavailable right now.";
        }
        Changed?.Invoke();
    }

    /// <summary>WMO weather code to a short description and an icon.</summary>
    public static (string Text, string Glyph) Describe(int code) => code switch
    {
        0 => ("Clear", Ui.Glyph.Sun),
        1 or 2 => ("Partly cloudy", Ui.Glyph.Cloud),
        3 => ("Cloudy", Ui.Glyph.Cloud),
        45 or 48 => ("Fog", Ui.Glyph.Fog),
        >= 51 and <= 67 => ("Rain", Ui.Glyph.Rain),
        >= 71 and <= 77 => ("Snow", Ui.Glyph.Snow),
        >= 80 and <= 82 => ("Showers", Ui.Glyph.Rain),
        85 or 86 => ("Snow showers", Ui.Glyph.Snow),
        >= 95 => ("Thunderstorm", Ui.Glyph.Storm),
        _ => ("", Ui.Glyph.Cloud),
    };
}

/// <summary>A single countdown timer.</summary>
internal sealed class CountdownTimer
{
    public DateTime? EndsAt { get; private set; }
    public TimeSpan Length { get; private set; }
    public bool Running => EndsAt is not null;
    public TimeSpan Left => EndsAt is { } e ? (e - DateTime.Now < TimeSpan.Zero ? TimeSpan.Zero : e - DateTime.Now) : TimeSpan.Zero;

    public event Action? Changed;
    public event Action? Finished;

    public void Start(TimeSpan length)
    {
        Length = length;
        EndsAt = DateTime.Now + length;
        Changed?.Invoke();
    }

    public void Cancel()
    {
        EndsAt = null;
        Changed?.Invoke();
    }

    /// <summary>Checked once a second by the island.</summary>
    public void Tick()
    {
        if (EndsAt is { } e && DateTime.Now >= e)
        {
            EndsAt = null;
            Changed?.Invoke();
            Finished?.Invoke();
        }
    }
}

/// <summary>Start with Windows, through the per-user Run key (no admin rights needed).</summary>
internal static class StartWithWindows
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "CinyNotch";

    public static bool IsOn
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) is string;
        }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true);
        if (k is null) return;
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(Name, throwOnMissingValue: false);
    }
}
