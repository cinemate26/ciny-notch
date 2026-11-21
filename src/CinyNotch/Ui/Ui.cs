using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CinyNotch.Ui;

/// <summary>Icon glyphs from Segoe Fluent Icons (Windows 11) / Segoe MDL2 Assets (Windows 10).</summary>
internal static class Glyph
{
    public const string Play = "", Pause = "", Previous = "", Next = "";
    public const string Mic = "", MicOff = "", Volume = "", Mute = "";
    public const string Brightness = "", Wifi = "", Bluetooth = "", NightLight = "";
    public const string Pin = "", Unpin = "", Delete = "", Copy = "";
    public const string Link = "", Photo = "", Document = "", Folder = "";
    public const string Phone = "", Clock = "", Timer = "", Add = "";
    public const string Accept = "", Cancel = "", Code = "", Camera = "";
    public const string Call = "", Settings = "", Repeat = "", Snooze = "";
    public const string Text = "", Music = "", Sun = "", Cloud = "";
    public const string Rain = "", Snow = "", Storm = "", Fog = "";
    public const string Ringer = "", Color = "", Screenshot = "", Warning = "";
    public const string Open = "", Battery = "", Sparkle = "", Up = "";
}

/// <summary>Small helpers so the panels read as layout, not boilerplate.</summary>
internal static class Ui
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string Primary = "#F6F6F8";
    public const string Secondary = "#D2D2D8";
    public const string Muted = "#9C9CA6";
    public const string Green = "#6EE7A8";
    public const string Amber = "#F5C26B";
    public const string Red = "#FF6B6B";

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static TextBlock Text(string text, double size = 13, string color = Primary,
        FontWeight weight = FontWeight.Light, bool trim = true)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = Brush(color),
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (trim) t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 14, string color = Primary)
    {
        var t = new TextBlock
        {
            Text = glyph,
            FontSize = size,
            Foreground = Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        t.Classes.Add("icon");
        return t;
    }

    public static Button Chip(string text, Action onClick, string? glyph = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(Icon(glyph, 11, Primary));
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = row;
        }
        var b = new Button { Content = content };
        b.Classes.Add("chip");
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button Round(string glyph, Action onClick, double size = 32, double iconSize = 14, string? tip = null, bool flat = false)
    {
        var b = new Button
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Content = Icon(glyph, iconSize, Primary),
        };
        b.Classes.Add("round");
        if (flat) b.Classes.Add("flat");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Changes the glyph inside a button made by <see cref="Round"/>.</summary>
    public static void SetGlyph(Button b, string glyph)
    {
        if (b.Content is TextBlock t) t.Text = glyph;
    }

    public static StackPanel Row(double spacing = 8, params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static StackPanel Column(double spacing = 4, params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static Border Dot(string color, double size = 7) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = Brush(color),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    public static string Ago(DateTime when)
    {
        var d = DateTime.Now - when;
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        return when.ToString("d MMM");
    }
}
