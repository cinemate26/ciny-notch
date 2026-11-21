using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CinyNotch.Services;
using CinyNotch.Ui;
using U = CinyNotch.Ui.Ui;

namespace CinyNotch.Island.Panels;

/// <summary>Clipboard: recent copies, pinned ones on top. Click a row to copy it again.</summary>
internal sealed class ClipboardPanel : IslandPanel
{
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _list = new() { Spacing = 5 };
    private readonly TextBlock _note = U.Text("", 12, U.Muted);
    private readonly Dictionary<string, Bitmap?> _thumbs = new();
    private ClipKind? _filter;

    public ClipboardPanel(Hub hub, IIslandHost host) : base(hub, host)
    {
        BuildFilters();
        var clear = U.Chip("Clear", () => Hub.Clipboard.ClearUnpinned(), Glyph.Delete);
        clear.HorizontalAlignment = HorizontalAlignment.Right;
        ToolTip.SetTip(clear, "Removes everything except pinned items");

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(_filters);
        Grid.SetColumn(clear, 1);
        head.Children.Add(clear);

        var scroll = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(18, 4, 18, 12) };
        grid.Children.Add(head);
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;

        hub.Clipboard.Changed += Refresh;
        Refresh();
    }

    public override string TabName => "Clipboard";
    public override double CardHeight => 320;

    public override void OnShown() => Refresh();

    private void BuildFilters()
    {
        _filters.Children.Clear();
        foreach (var (label, kind) in new (string, ClipKind?)[] { ("All", null), ("Text", ClipKind.Text), ("Links", ClipKind.Link), ("Pictures", ClipKind.Image), ("Files", ClipKind.Files) })
        {
            var chip = U.Chip(label, () => { _filter = kind; BuildFilters(); Refresh(); });
            if (_filter == kind) chip.Classes.Add("on");
            _filters.Children.Add(chip);
        }
    }

    private void Refresh()
    {
        _list.Children.Clear();
        var items = Hub.Clipboard.Items
            .Where(i => _filter is null || i.Kind == _filter)
            .OrderByDescending(i => i.Pinned)
            .ThenByDescending(i => i.Time)
            .Take(40)
            .ToList();

        if (items.Count == 0)
        {
            var t = U.Text(Hub.Clipboard.Items.Count == 0
                ? "Copy something and it appears here. Private copies from password managers are never kept."
                : "Nothing of this kind yet.", 13, U.Secondary, trim: false);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(4, 16, 4, 0);
            _list.Children.Add(t);
            return;
        }
        foreach (var item in items) _list.Children.Add(Row(item));
    }

    private Control Row(ClipItem item)
    {
        var glyph = item.Kind switch
        {
            ClipKind.Link => Glyph.Link,
            ClipKind.Image => Glyph.Photo,
            ClipKind.Files => Glyph.Document,
            _ => Glyph.Text,
        };

        Control preview;
        if (item.Kind == ClipKind.Image && Thumb(item) is { } bmp)
        {
            preview = U.Row(10,
                new Border { Width = 64, Height = 40, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill } },
                U.Text(item.Text, 12, U.Secondary));
        }
        else
        {
            var text = item.Text.ReplaceLineEndings(" ").Trim();
            var t = U.Text(text.Length > 220 ? text[..220] : text, 13);
            t.MaxLines = 2;
            t.TextWrapping = TextWrapping.Wrap;
            preview = t;
        }

        var when = U.Text(U.Ago(item.Time), 11, U.Muted);
        var pin = U.Round(item.Pinned ? Glyph.Unpin : Glyph.Pin, () => Hub.Clipboard.TogglePin(item), 26, 11, item.Pinned ? "Unpin" : "Pin", flat: true);
        if (item.Pinned) pin.Classes.Add("on");
        var remove = U.Round(Glyph.Delete, () => Hub.Clipboard.Remove(item), 26, 11, "Remove", flat: true);
        var actions = U.Row(2, when, pin, remove);
        actions.VerticalAlignment = VerticalAlignment.Center;
        when.Margin = new Thickness(0, 0, 6, 0);

        var icon = U.Icon(glyph, 13, U.Muted);
        icon.Margin = new Thickness(0, 0, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 10, 0);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(icon);
        Grid.SetColumn(preview, 1);
        grid.Children.Add(preview);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var row = new Border { Child = grid };
        row.Classes.Add("row");
        ToolTip.SetTip(row, "Click to copy");
        row.Tapped += async (_, e) =>
        {
            if (e.Source is Visual v && IsInside(v, actions)) return;
            await Hub.Clipboard.CopyAsync(item);
            row.Background = U.Brush("#33FFFFFF");
            _note.Text = "Copied";
        };
        return row;
    }

    private static bool IsInside(Visual v, Visual container)
    {
        for (Visual? p = v; p is not null; p = p.GetVisualParent()) if (p == container) return true;
        return false;
    }

    private Bitmap? Thumb(ClipItem item)
    {
        if (item.ImageFile is null) return null;
        if (_thumbs.TryGetValue(item.ImageFile, out var b)) return b;
        try
        {
            using var s = File.OpenRead(item.ImageFile);
            b = Bitmap.DecodeToWidth(s, 128);
        }
        catch
        {
            b = null;
        }
        _thumbs[item.ImageFile] = b;
        return b;
    }
}
