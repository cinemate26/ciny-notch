using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CinyNotch.Services;
using CinyNotch.Ui;
using U = CinyNotch.Ui.Ui;

namespace CinyNotch.Island.Panels;

/// <summary>Reminders: type it, pick when, done. Due reminders pop up on the island.</summary>
internal sealed class RemindersPanel : IslandPanel
{
    private readonly TextBox _input = new()
    {
        PlaceholderText = "Remind me to...",
        FontSize = 13,
        FontWeight = FontWeight.Light,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(12, 7),
        MinHeight = 0,
    };
    private readonly StackPanel _when = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _repeat = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _list = new() { Spacing = 5 };

    private (string Label, Func<DateTime> At) _choice;
    private Repeat _repeatChoice = Repeat.Once;

    private static readonly (string Label, Func<DateTime> At)[] Times =
    [
        ("5 min", () => DateTime.Now.AddMinutes(5)),
        ("15 min", () => DateTime.Now.AddMinutes(15)),
        ("30 min", () => DateTime.Now.AddMinutes(30)),
        ("1 hour", () => DateTime.Now.AddHours(1)),
        ("Tomorrow 9:00", () => DateTime.Today.AddDays(1).AddHours(9)),
    ];

    public RemindersPanel(Hub hub, IIslandHost host) : base(hub, host)
    {
        _choice = Times[1];
        _input.AddHandler(PointerPressedEvent, (_, _) => Host.EnableKeyboard(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { AddReminder(); e.Handled = true; }
        };

        var add = U.Round(Glyph.Add, AddReminder, 32, 13, "Add reminder");
        var inputRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        inputRow.Children.Add(_input);
        add.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(add, 1);
        inputRow.Children.Add(add);

        BuildChoices();
        var choices = U.Row(14, _when, _repeat);
        choices.Margin = new Thickness(0, 8, 0, 10);

        var scroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(18, 6, 18, 12) };
        grid.Children.Add(inputRow);
        Grid.SetRow(choices, 1);
        grid.Children.Add(choices);
        Grid.SetRow(scroll, 2);
        grid.Children.Add(scroll);
        Content = grid;

        hub.Reminders.Changed += Refresh;
        Refresh();
    }

    public override string TabName => "Reminders";
    public override double CardHeight => 320;

    public override void OnShown() => Refresh();

    public override void OnTick()
    {
        // Keep "in 4 min" style labels fresh without rebuilding every tick.
        if (DateTime.Now.Second % 15 == 0) Refresh();
    }

    private void BuildChoices()
    {
        _when.Children.Clear();
        foreach (var t in Times)
        {
            var chip = U.Chip(t.Label, () => { _choice = t; BuildChoices(); });
            if (_choice.Label == t.Label) chip.Classes.Add("on");
            _when.Children.Add(chip);
        }
        _repeat.Children.Clear();
        foreach (var r in new[] { Repeat.Once, Repeat.Daily, Repeat.Weekly })
        {
            var chip = U.Chip(r.ToString(), () => { _repeatChoice = r; BuildChoices(); });
            if (_repeatChoice == r) chip.Classes.Add("on");
            _repeat.Children.Add(chip);
        }
    }

    private void AddReminder()
    {
        var text = _input.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        Hub.Reminders.Add(text, _choice.At(), _repeatChoice);
        _input.Text = "";
    }

    private void Refresh()
    {
        _list.Children.Clear();
        if (Hub.Reminders.Items.Count == 0)
        {
            var t = U.Text("No reminders yet. Type one above, pick a time, press Enter.", 13, U.Secondary, trim: false);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(4, 6, 4, 0);
            _list.Children.Add(t);
            return;
        }
        foreach (var r in Hub.Reminders.Items) _list.Children.Add(Row(r));
    }

    private Control Row(Reminder r)
    {
        var text = U.Text(r.Text, 13);
        var due = U.Text(DueLabel(r), 11, r.Due <= DateTime.Now ? U.Amber : U.Muted);
        var left = U.Column(1, text, due);
        if (r.Repeat != Repeat.Once)
        {
            due.Text += $"  ·  {r.Repeat.ToString().ToLowerInvariant()}";
        }

        var done = U.Round(Glyph.Accept, () => Hub.Reminders.Done(r), 26, 11, "Done", flat: true);
        var delete = U.Round(Glyph.Delete, () => Hub.Reminders.Delete(r), 26, 11, "Delete", flat: true);
        var actions = U.Row(2, done, delete);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        var row = new Border { Child = grid, Cursor = new Cursor(StandardCursorType.Arrow) };
        row.Classes.Add("row");
        return row;
    }

    public static string DueLabel(Reminder r)
    {
        var d = r.Due;
        var now = DateTime.Now;
        if (d <= now) return "Now";
        var left = d - now;
        if (left.TotalMinutes < 60) return $"In {Math.Ceiling(left.TotalMinutes)} min  ·  {d:HH:mm}";
        if (d.Date == now.Date) return $"Today {d:HH:mm}";
        if (d.Date == now.Date.AddDays(1)) return $"Tomorrow {d:HH:mm}";
        return d.ToString("ddd d MMM, HH:mm");
    }
}
