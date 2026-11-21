using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CinyNotch.Services;
using CinyNotch.Ui;
using U = CinyNotch.Ui.Ui;

namespace CinyNotch.Settings;

/// <summary>A small, ordinary window for the few choices the island has.</summary>
internal sealed class SettingsWindow : Window
{
    private readonly Hub _hub;
    private readonly Action _onSaved;
    private readonly StackPanel _body = new() { Spacing = 18, Margin = new Thickness(26, 22, 26, 26) };

    public SettingsWindow(Hub hub, Action onSaved)
    {
        _hub = hub;
        _onSaved = onSaved;
        Title = "Ciny Notch settings";
        Width = 470;
        Height = 640;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = U.Brush("#141418");
        // Satoshi if it was added to Assets/Fonts (see README), otherwise the Windows UI font.
        FontFamily = new FontFamily("avares://CinyNotch/Assets/Fonts#Satoshi, Segoe UI Variable Text, Segoe UI");
        FontWeight = FontWeight.Light;
        Content = new ScrollViewer { Content = _body };
        Build();
    }

    private AppSettings S => _hub.Settings;

    private void Build()
    {
        _body.Children.Clear();
        _body.Children.Add(U.Text("Settings", 26));

        _body.Children.Add(Section("Shape",
            Choices([("Notch", S.Shape == IslandShape.Notch, () => { S.Shape = IslandShape.Notch; Save(); }),
                     ("Floating", S.Shape == IslandShape.Floating, () => { S.Shape = IslandShape.Floating; Save(); })])));

        _body.Children.Add(Section("Look",
            Choices([("Glass", S.Look == IslandLook.Glass, () => { S.Look = IslandLook.Glass; Save(); }),
                     ("Dark", S.Look == IslandLook.Dark, () => { S.Look = IslandLook.Dark; Save(); }),
                     ("Black", S.Look == IslandLook.Black, () => { S.Look = IslandLook.Black; Save(); })])));

        var city = new TextBox { Text = S.WeatherCity, PlaceholderText = "City, for example Mumbai", Width = 260, FontWeight = FontWeight.Light, CornerRadius = new CornerRadius(10) };
        var saveCity = U.Chip("Save", () => { S.WeatherCity = city.Text?.Trim() ?? ""; Save(); });
        _body.Children.Add(Section("Weather city", U.Row(8, city, saveCity),
            "Only the city name is sent, to the free Open-Meteo service."));

        _body.Children.Add(Toggle("Synced lyrics", "Looks up lyrics on lrclib.net. Sends only the song's title, artist and album.",
            S.Lyrics, v => S.Lyrics = v));
        _body.Children.Add(Toggle("Claude Code status", "Reads Claude Code's own session logs on this PC to show what it's doing. Nothing leaves the PC.",
            S.ClaudeStatus, v => S.ClaudeStatus = v));
        _body.Children.Add(Toggle("Claude alerts", "A short pop-up when Claude finishes, or seems to be waiting for you.",
            S.ClaudeAlerts, v => S.ClaudeAlerts = v));
        _body.Children.Add(Toggle("Start with Windows", "Opens the island when you sign in.",
            StartWithWindows.IsOn, v => StartWithWindows.Set(v)));

        var shortcuts = U.Text(
            $"Ctrl+Alt+N  open or close\nCtrl+Alt+P  play or pause\n{_hub.MicShortcut}  mute every microphone\nCtrl+Alt+H  hide or show the island\nCtrl+Alt+S  screenshot\nCtrl+Alt+C  copy the colour under the pointer",
            13, U.Secondary, trim: false);
        shortcuts.LineHeight = 22;
        _body.Children.Add(Section("Shortcuts", shortcuts));

        _body.Children.Add(Section("Your data",
            U.Row(8,
                U.Chip("Clear clipboard history", () => _hub.Clipboard.ClearUnpinned(), Glyph.Delete),
                U.Chip("Open data folder", OpenDataFolder, Glyph.Folder)),
            "Clipboard history and reminders stay in this folder, on this PC only."));
    }

    private static Control Section(string title, Control content, string? note = null)
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(U.Text(title, 13, U.Muted));
        s.Children.Add(content);
        if (note is not null)
        {
            var n = U.Text(note, 12, U.Muted, trim: false);
            n.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(n);
        }
        return s;
    }

    private static Control Choices((string Label, bool On, Action Pick)[] options)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (label, on, pick) in options)
        {
            var chip = U.Chip(label, pick);
            if (on) chip.Classes.Add("on");
            row.Children.Add(chip);
        }
        return row;
    }

    private Control Toggle(string title, string note, bool value, Action<bool> set)
    {
        var chip = U.Chip(value ? "On" : "Off", () => { set(!value); Save(); });
        if (value) chip.Classes.Add("on");
        chip.MinWidth = 52;
        chip.HorizontalContentAlignment = HorizontalAlignment.Center;

        var text = U.Column(2, U.Text(title, 15), U.Text(note, 12, U.Muted, trim: false));
        ((TextBlock)text.Children[1]).TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(0, 0, 16, 0);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        chip.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chip, 1);
        grid.Children.Add(chip);
        return grid;
    }

    private void Save()
    {
        S.Save();
        _hub.ApplySettings();
        _onSaved();
        Build();
    }

    private static void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(Store.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Store.Folder}\"") { UseShellExecute = true });
        }
        catch { }
    }
}
