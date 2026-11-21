using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CinyNotch.Services;
using CinyNotch.Ui;
using U = CinyNotch.Ui.Ui;

namespace CinyNotch.Island.Panels;

/// <summary>Controls: volume, brightness, Wi-Fi, Bluetooth, microphone and a few tools.</summary>
internal sealed class ControlsPanel : IslandPanel
{
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100 };
    private readonly TextBlock _volumeText = U.Text("", 12, U.Secondary);
    private readonly Button _mute;
    private readonly Slider _brightness = new() { Minimum = 0, Maximum = 100 };
    private readonly TextBlock _brightnessText = U.Text("", 12, U.Secondary);
    private readonly Control _brightnessRow;
    private readonly TextBlock _brightnessNote = U.Text("Brightness can't be changed on this screen from the PC.", 12, U.Muted);
    private readonly Button _wifi;
    private readonly Button _bluetooth;
    private readonly Button _mic;
    private bool _updating;

    public ControlsPanel(Hub hub, IIslandHost host, Action pickColour) : base(hub, host)
    {
        _mute = U.Round(Glyph.Volume, () => Hub.Audio.ToggleSpeakerMute(), 32, 13, "Mute");
        _volume.ValueChanged += (_, e) => { if (!_updating) Hub.Audio.SetVolume((float)(e.NewValue / 100)); _volumeText.Text = $"{Math.Round(e.NewValue)}%"; };
        _brightness.ValueChanged += (_, e) => { if (!_updating) Hub.Brightness.Set((int)e.NewValue); _brightnessText.Text = $"{Math.Round(e.NewValue)}%"; };

        var volumeRow = SliderRow(_mute, _volume, _volumeText);
        _brightnessRow = SliderRow(U.Round(Glyph.Brightness, () => { }, 32, 13), _brightness, _brightnessText);

        _wifi = Toggle(Glyph.Wifi, "Wi-Fi", () => _ = Hub.Radios.ToggleWifiAsync());
        _bluetooth = Toggle(Glyph.Bluetooth, "Bluetooth", () => _ = Hub.Radios.ToggleBluetoothAsync());
        _mic = Toggle(Glyph.Mic, "Microphone (Ctrl+Alt+M)", () => Hub.Audio.ToggleMicMute());
        var night = Toggle(Glyph.NightLight, "Night light settings", () => Launch("ms-settings:nightlight"));
        var shot = Toggle(Glyph.Screenshot, "Screenshot (Ctrl+Alt+S)", () => { Host.Close(); Launch("ms-screenclip:"); });
        var colour = Toggle(Glyph.Color, "Colour picker (Ctrl+Alt+C)", () => { Host.Close(); pickColour(); });
        var toggles = U.Row(8, _wifi, _bluetooth, _mic, night, shot, colour);
        toggles.Margin = new Thickness(0, 12, 0, 0);
        toggles.HorizontalAlignment = HorizontalAlignment.Left;

        Content = new StackPanel
        {
            Margin = new Thickness(22, 10, 22, 14),
            Spacing = 4,
            Children = { volumeRow, _brightnessRow, _brightnessNote, toggles },
        };

        hub.Audio.Changed += Refresh;
        hub.Radios.Changed += Refresh;
        hub.Brightness.Changed += Refresh;
        Refresh();
    }

    public override string TabName => "Controls";
    public override double CardHeight => 214;

    public override void OnShown() => Refresh();

    private static Control SliderRow(Control icon, Slider slider, TextBlock value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,44") };
        grid.Children.Add(icon);
        slider.Margin = new Thickness(12, 0, 10, 0);
        slider.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    private static Button Toggle(string glyph, string tip, Action onClick) => U.Round(glyph, onClick, 40, 15, tip);

    private void Refresh()
    {
        _updating = true;
        _volume.Value = Math.Round(Hub.Audio.Volume * 100);
        _volumeText.Text = $"{Math.Round(Hub.Audio.Volume * 100)}%";
        U.SetGlyph(_mute, Hub.Audio.SpeakerMuted ? Glyph.Mute : Glyph.Volume);
        SetOn(_mute, Hub.Audio.SpeakerMuted);

        _brightnessRow.IsVisible = Hub.Brightness.Supported;
        _brightnessNote.IsVisible = !Hub.Brightness.Supported;
        _brightness.Value = Hub.Brightness.Level;
        _brightnessText.Text = $"{Hub.Brightness.Level}%";
        _updating = false;

        _wifi.IsVisible = Hub.Radios.HasWifi;
        _bluetooth.IsVisible = Hub.Radios.HasBluetooth;
        SetOn(_wifi, Hub.Radios.WifiOn);
        SetOn(_bluetooth, Hub.Radios.BluetoothOn);
        U.SetGlyph(_mic, Hub.Audio.MicMuted ? Glyph.MicOff : Glyph.Mic);
        _mic.Classes.Set("alert", Hub.Audio.MicMuted);
    }

    private static void SetOn(Button b, bool on) => b.Classes.Set("on", on);

    public static void Launch(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }
}
