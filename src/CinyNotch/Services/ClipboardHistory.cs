using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CinyNotch.Diagnostics;
using CinyNotch.Native;

namespace CinyNotch.Services;

internal enum ClipKind { Text, Link, Image, Files }

internal sealed class ClipItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipKind Kind { get; set; }
    public string Text { get; set; } = "";
    public List<string> Paths { get; set; } = new();
    public string? ImageFile { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }
}

/// <summary>
/// Remembers what you copy: text, links, pictures and files. Kept only on this PC, in
/// the project's data folder. Anything a password manager marks as private is never recorded.
/// </summary>
internal sealed class ClipboardHistory
{
    private const int MaxUnpinned = 60;
    private const string FileName = "clipboard.json";

    private readonly string _imageFolder = Store.PathOf("clips");
    private readonly DispatcherTimer _debounce;
    private IClipboard? _clipboard;
    private IStorageProvider? _storage;
    private bool _ignoreNext;

    public List<ClipItem> Items { get; }
    public event Action? Changed;

    public ClipboardHistory()
    {
        Items = Store.Load<List<ClipItem>>(FileName);
        Directory.CreateDirectory(_imageFolder);
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Background, (_, _) =>
        {
            _debounce!.Stop();
            _ = CaptureAsync();
        });
    }

    public void Attach(TopLevel top)
    {
        _clipboard = top.Clipboard;
        _storage = top.StorageProvider;
    }

    /// <summary>Called on every clipboard change; waits briefly so multi-format copies settle.</summary>
    public void OnClipboardChanged()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task CaptureAsync()
    {
        if (_clipboard is null) return;
        if (_ignoreNext) { _ignoreNext = false; return; }
        if (IsPrivate()) { Log.Write("clipboard: private copy skipped"); return; }

        try
        {
            var files = await _clipboard.TryGetFilesAsync();
            if (files is { Length: > 0 })
            {
                var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
                if (paths.Count > 0) { Add(new ClipItem { Kind = ClipKind.Files, Paths = paths, Text = string.Join(", ", paths.Select(Path.GetFileName)) }); return; }
            }

            var bitmap = await _clipboard.TryGetBitmapAsync();
            if (bitmap is not null)
            {
                var file = Path.Combine(_imageFolder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
                bitmap.Save(file);
                Add(new ClipItem { Kind = ClipKind.Image, ImageFile = file, Text = $"Picture {bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}" });
                return;
            }

            var text = await _clipboard.TryGetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var trimmed = text.Trim();
                var isLink = Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") && !trimmed.Contains(' ');
                Add(new ClipItem { Kind = isLink ? ClipKind.Link : ClipKind.Text, Text = text });
            }
        }
        catch (Exception ex)
        {
            Log.Write($"clipboard read: {ex.Message}");
        }
    }

    private void Add(ClipItem item)
    {
        // The same thing copied twice moves to the top instead of repeating.
        var same = Items.FirstOrDefault(i => i.Kind == item.Kind && i.Text == item.Text && i.Kind != ClipKind.Image);
        if (same is not null)
        {
            Items.Remove(same);
            same.Time = DateTime.Now;
            Items.Insert(0, same);
        }
        else
        {
            Items.Insert(0, item);
        }
        Trim();
        Save();
    }

    private void Trim()
    {
        var unpinned = Items.Where(i => !i.Pinned).Skip(MaxUnpinned).ToList();
        foreach (var old in unpinned)
        {
            Items.Remove(old);
            DeleteImage(old);
        }
    }

    public async Task CopyAsync(ClipItem item)
    {
        if (_clipboard is null) return;
        _ignoreNext = true;
        try
        {
            switch (item.Kind)
            {
                case ClipKind.Text:
                case ClipKind.Link:
                    await _clipboard.SetTextAsync(item.Text);
                    break;
                case ClipKind.Image when item.ImageFile is not null && File.Exists(item.ImageFile):
                    await _clipboard.SetBitmapAsync(new Bitmap(item.ImageFile));
                    break;
                case ClipKind.Files when _storage is not null:
                    var storageItems = new List<IStorageItem>();
                    foreach (var p in item.Paths)
                    {
                        var f = await _storage.TryGetFileFromPathAsync(p);
                        if (f is not null) storageItems.Add(f);
                    }
                    if (storageItems.Count > 0) await _clipboard.SetFilesAsync(storageItems);
                    break;
            }
            Items.Remove(item);
            item.Time = DateTime.Now;
            Items.Insert(0, item);
            Save();
        }
        catch (Exception ex)
        {
            _ignoreNext = false;
            Log.Write($"clipboard write: {ex.Message}");
        }
    }

    public void TogglePin(ClipItem item)
    {
        item.Pinned = !item.Pinned;
        Save();
    }

    public void Remove(ClipItem item)
    {
        Items.Remove(item);
        DeleteImage(item);
        Save();
    }

    public void ClearUnpinned()
    {
        foreach (var item in Items.Where(i => !i.Pinned).ToList())
        {
            Items.Remove(item);
            DeleteImage(item);
        }
        Save();
    }

    private static void DeleteImage(ClipItem item)
    {
        try { if (item.ImageFile is not null && File.Exists(item.ImageFile)) File.Delete(item.ImageFile); } catch { }
    }

    private void Save()
    {
        Store.Save(FileName, Items);
        Changed?.Invoke();
    }

    /// <summary>
    /// Password managers and other careful apps tag private copies with these formats, which
    /// ask clipboard history tools to stay away. Windows' own clipboard history honours them too.
    /// </summary>
    private static bool IsPrivate()
    {
        if (Win32.IsClipboardFormatAvailable(Win32.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"))) return true;
        if (Win32.IsClipboardFormatAvailable(Win32.RegisterClipboardFormat("Clipboard Viewer Ignore"))) return true;

        var canInclude = Win32.RegisterClipboardFormat("CanIncludeInClipboardHistory");
        if (!Win32.IsClipboardFormatAvailable(canInclude)) return false;
        if (!Win32.OpenClipboard(0)) return true; // can't check: stay on the safe side
        try
        {
            var handle = Win32.GetClipboardData(canInclude);
            if (handle == 0) return false;
            var p = Win32.GlobalLock(handle);
            try { return p != 0 && Marshal.ReadInt32(p) == 0; }
            finally { Win32.GlobalUnlock(handle); }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }
}
