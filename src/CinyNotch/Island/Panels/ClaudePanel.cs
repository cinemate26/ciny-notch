using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CinyNotch.Services;
using CinyNotch.Ui;
using U = CinyNotch.Ui.Ui;

namespace CinyNotch.Island.Panels;

/// <summary>Claude: every Claude Code session from the last 20 minutes and what it's doing.</summary>
internal sealed class ClaudePanel : IslandPanel
{
    private readonly StackPanel _list = new() { Spacing = 6 };

    public ClaudePanel(Hub hub, IIslandHost host) : base(hub, host)
    {
        Content = new ScrollViewer
        {
            Margin = new Thickness(18, 6, 18, 14),
            Content = _list,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        hub.Claude.Changed += Refresh;
        Refresh();
    }

    public override string TabName => "Claude";
    public override double CardHeight => 256;

    public override void OnShown() => Refresh();

    public static (string Color, string Label) Describe(ClaudeState s) => s switch
    {
        ClaudeState.Working => (U.Green, "Working"),
        ClaudeState.Waiting => (U.Amber, "Waiting for you or a long task"),
        _ => (U.Muted, "Finished"),
    };

    private void Refresh()
    {
        _list.Children.Clear();
        if (!Hub.Settings.ClaudeStatus)
        {
            _list.Children.Add(Empty("Claude Code status is off", "Turn it on in Settings."));
            return;
        }
        var sessions = Hub.Claude.Sessions;
        if (sessions.Count == 0)
        {
            _list.Children.Add(Empty("No Claude Code activity", "Sessions from the terminal or VS Code show up here while they run, and for 10 minutes after."));
            return;
        }
        foreach (var s in sessions.Take(6)) _list.Children.Add(Row(s));
    }

    private static Control Empty(string title, string note)
    {
        var n = U.Text(note, 13, U.Secondary, trim: false);
        n.TextWrapping = TextWrapping.Wrap;
        var c = U.Column(4, U.Text(title, 22), n);
        c.Margin = new Thickness(8, 26, 8, 0);
        return c;
    }

    private static Control Row(ClaudeSession s)
    {
        var (color, label) = Describe(s.State);
        var title = U.Text(s.Title, 14, U.Primary, FontWeight.Regular);
        var when = U.Text(s.State == ClaudeState.Done ? Ui.Ui.Ago(s.LastActivity) : "now", 11, U.Muted);
        when.HorizontalAlignment = HorizontalAlignment.Right;

        // Meta line: [VS Code logo] VS Code · project · state
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var sourceLogo = s.Source == "VS Code" ? AppIcons.VsCode : null;
        if (sourceLogo is not null) meta.Children.Add(new Image { Source = sourceLogo, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center });
        else meta.Children.Add(U.Icon(Glyph.Code, 11, U.Secondary));
        meta.Children.Add(U.Text($"{s.Source}  ·  {s.Project}  ·  ", 12, U.Secondary));
        meta.Children.Add(U.Text(label, 12, color));

        // The Claude logo, with the session's state as a dot on its corner.
        var logo = new Panel { Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        if (AppIcons.Claude is { } claude)
            logo.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = new Image { Source = claude, Stretch = Stretch.UniformToFill } });
        else
            logo.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = U.Brush("#26FFFFFF"), Child = U.Icon(Glyph.Sparkle, 14) });
        var dot = new Border
        {
            Width = 11, Height = 11, CornerRadius = new CornerRadius(5.5), Background = U.Brush(color),
            BorderBrush = U.Brush("#1A1A1F"), BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -3, -3),
        };
        logo.Children.Add(dot);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(logo);
        var text = U.Column(2, title, meta);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(when, 2);
        grid.Children.Add(when);

        var row = new Border { Child = grid, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow) };
        row.Classes.Add("row");
        return row;
    }
}
