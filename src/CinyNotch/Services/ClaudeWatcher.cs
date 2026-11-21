using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using CinyNotch.Diagnostics;

namespace CinyNotch.Services;

internal enum ClaudeState { Working, Waiting, Done }

internal sealed record ClaudeSession(
    string Id, string Title, string Project, string Source, ClaudeState State, DateTime LastActivity);

/// <summary>
/// Shows what Claude Code is doing, read-only. Every Claude Code session (terminal or the
/// VS Code extension) writes a log under %UserProfile%\.claude\projects. This watcher reads
/// only the end of recently changed logs and works out, per session:
///   Working: Claude is writing, or a tool just started.
///   Waiting: a tool has been running a while with no news, which usually means Claude is
///            waiting for you to approve something (or a long build is running).
///   Done:    Claude finished its turn.
/// Nothing is changed and nothing leaves the PC.
/// </summary>
internal sealed class ClaudeWatcher
{
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DoneShownFor = TimeSpan.FromMinutes(10);
    // Builds and tests can run a while without writing; only a longer silence counts as waiting.
    private static readonly TimeSpan QuietToolIsWaiting = TimeSpan.FromSeconds(30);
    private const int TailBytes = 512 * 1024;

    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, (DateTime Stamp, long Length, Parsed Info)> _cache = new();
    private readonly Dictionary<string, ClaudeState> _lastState = new();

    // Session logs that changed recently. Filled by a folder watcher (cheap: Windows tells us
    // which file changed) plus a full listing once a minute as a safety net. Checking every
    // log every two seconds cost a quarter of a second each time on a busy PC.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _hot = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private DateTime _lastFullScan = DateTime.MinValue;
    private int _busy;

    public IReadOnlyList<ClaudeSession> Sessions { get; private set; } = [];
    public bool Enabled { get; set; } = true;

    public event Action? Changed;
    /// <summary>A session went from working to done, or to waiting for you.</summary>
    public event Action<ClaudeSession>? Finished;
    public event Action<ClaudeSession>? NeedsYou;

    public ClaudeWatcher()
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Poll());
    }

    public void Start()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                _watcher = new FileSystemWatcher(_root, "*.jsonl")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                _watcher.Changed += (_, e) => MarkHot(e.FullPath);
                _watcher.Created += (_, e) => MarkHot(e.FullPath);
                _watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"claude folder watch: {ex.Message}");
        }
        Poll();
        _timer.Start();
    }

    /// <summary>Only top-level session logs count (sub-agent logs live in subfolders).</summary>
    private void MarkHot(string path)
    {
        if (string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(path)), _root, StringComparison.OrdinalIgnoreCase))
            _hot[path] = 0;
    }

    private sealed record Parsed(string Title, string Cwd, string Entry, string LastType, string? StopReason);

    private void Poll()
    {
        if (!Enabled || !Directory.Exists(_root))
        {
            if (Sessions.Count > 0) { Sessions = []; Changed?.Invoke(); }
            return;
        }
        // The reading happens off the UI thread; one pass at a time.
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                var sessions = Scan();
                Dispatcher.UIThread.Post(() => Apply(sessions));
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private List<ClaudeSession> Scan()
    {
        var now = DateTime.Now;
        var sessions = new List<ClaudeSession>();
        try
        {
            if (now - _lastFullScan > TimeSpan.FromMinutes(1))
            {
                _lastFullScan = now;
                // DirectoryInfo listing carries each file's time with it: no extra disk call per file.
                foreach (var dir in new DirectoryInfo(_root).EnumerateDirectories())
                    foreach (var f in dir.EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly))
                        if (now - f.LastWriteTime <= ActiveWindow) _hot[f.FullName] = 0;
            }

            foreach (var path in _hot.Keys)
            {
                var info = new FileInfo(path);
                if (!info.Exists || now - info.LastWriteTime > ActiveWindow)
                {
                    _hot.TryRemove(path, out _);
                    _cache.Remove(path);
                    continue;
                }

                var parsed = ParseCached(info);
                if (parsed is null) continue;

                var state = StateOf(parsed, now - info.LastWriteTime);
                if (state == ClaudeState.Done && now - info.LastWriteTime > DoneShownFor) continue;

                var project = string.IsNullOrEmpty(parsed.Cwd) ? "Claude Code" : Path.GetFileName(parsed.Cwd.TrimEnd('\\', '/'));
                var title = string.IsNullOrWhiteSpace(parsed.Title) ? project : parsed.Title;
                var source = parsed.Entry.Contains("vscode", StringComparison.OrdinalIgnoreCase) ? "VS Code"
                    : parsed.Entry.Contains("cli", StringComparison.OrdinalIgnoreCase) ? "Terminal" : "Claude Code";
                sessions.Add(new ClaudeSession(Path.GetFileNameWithoutExtension(path), title, project, source, state, info.LastWriteTime));
            }
        }
        catch (Exception ex)
        {
            Log.Write($"claude watcher: {ex.Message}");
        }

        sessions.Sort((a, b) => b.LastActivity.CompareTo(a.LastActivity));
        return sessions;
    }

    private void Apply(List<ClaudeSession> sessions)
    {
        foreach (var s in sessions)
        {
            if (_lastState.TryGetValue(s.Id, out var before) && before != s.State)
            {
                if (s.State == ClaudeState.Done && before != ClaudeState.Done) Finished?.Invoke(s);
                else if (s.State == ClaudeState.Waiting && before == ClaudeState.Working) NeedsYou?.Invoke(s);
            }
            _lastState[s.Id] = s.State;
        }

        var changed = sessions.Count != Sessions.Count || sessions.Where((s, i) => s != Sessions[i]).Any();
        Sessions = sessions;
        if (changed) Changed?.Invoke();
    }

    private static ClaudeState StateOf(Parsed p, TimeSpan quiet)
    {
        if (p.LastType == "assistant")
        {
            if (p.StopReason is "end_turn" or "stop_sequence" or "max_tokens") return ClaudeState.Done;
            if (p.StopReason == "tool_use") return quiet > QuietToolIsWaiting ? ClaudeState.Waiting : ClaudeState.Working;
            return ClaudeState.Working; // still streaming
        }
        // Last word was the user's (a prompt or a tool result): Claude is thinking, unless it went quiet long ago.
        return quiet > TimeSpan.FromMinutes(3) ? ClaudeState.Done : ClaudeState.Working;
    }

    private Parsed? ParseCached(FileInfo info)
    {
        if (_cache.TryGetValue(info.FullName, out var c) && c.Stamp == info.LastWriteTime && c.Length == info.Length)
            return c.Info;
        var parsed = ParseTail(info);
        if (parsed is not null) _cache[info.FullName] = (info.LastWriteTime, info.Length, parsed);
        return parsed;
    }

    /// <summary>Reads only the last part of the log, newest line first.</summary>
    private static Parsed? ParseTail(FileInfo info)
    {
        string text;
        try
        {
            using var fs = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, fs.Length - TailBytes);
            fs.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[fs.Length - start];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = fs.Read(buffer, read, buffer.Length - read);
                if (n == 0) break;
                read += n;
            }
            text = Encoding.UTF8.GetString(buffer, 0, read);
            if (start > 0)
            {
                var firstBreak = text.IndexOf('\n');
                text = firstBreak >= 0 ? text[(firstBreak + 1)..] : "";
            }
        }
        catch
        {
            return null;
        }

        string? title = null, cwd = null, entry = null, lastType = null, stop = null;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            // Cheap filters first: most lines are of no interest.
            var isTitle = title is null && line.Contains("\"ai-title\"");
            var isTurn = lastType is null && (line.Contains("\"type\":\"assistant\"") || line.Contains("\"type\":\"user\""));
            if (!isTitle && !isTurn) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "ai-title" && title is null)
                {
                    title = root.TryGetProperty("aiTitle", out var at) ? at.GetString() : null;
                }
                else if ((type is "assistant" or "user") && lastType is null)
                {
                    if (root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) continue;
                    lastType = type;
                    cwd = root.TryGetProperty("cwd", out var cw) ? cw.GetString() : null;
                    entry = root.TryGetProperty("entrypoint", out var ep) ? ep.GetString() : null;
                    if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("stop_reason", out var sr) && sr.ValueKind == JsonValueKind.String)
                        stop = sr.GetString();
                }
            }
            catch
            {
                // A line cut in half by the writer: skip it.
            }
            if (lastType is not null && title is not null) break;
        }

        return lastType is null ? null : new Parsed(title ?? "", cwd ?? "", entry ?? "", lastType, stop);
    }
}
