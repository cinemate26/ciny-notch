namespace CinyNotch.Services;

internal enum Repeat { Once, Daily, Weekly }

internal sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Due { get; set; }
    public Repeat Repeat { get; set; }
}

/// <summary>Quick reminders that pop up on the island, with snooze, done and repeat.</summary>
internal sealed class Reminders
{
    private const string FileName = "reminders.json";
    private readonly HashSet<string> _alerted = new();

    public List<Reminder> Items { get; }
    public event Action? Changed;
    /// <summary>A reminder came due.</summary>
    public event Action<Reminder>? Due;

    public Reminders()
    {
        Items = Store.Load<List<Reminder>>(FileName);
        Sort();
    }

    public Reminder Add(string text, DateTime due, Repeat repeat)
    {
        var r = new Reminder { Text = text.Trim(), Due = due, Repeat = repeat };
        Items.Add(r);
        Sort();
        Save();
        return r;
    }

    /// <summary>Checked once a second by the island.</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        foreach (var r in Items.ToList())
        {
            if (r.Due <= now && _alerted.Add(r.Id + r.Due.Ticks)) Due?.Invoke(r);
        }
    }

    public void Snooze(Reminder r, TimeSpan by)
    {
        r.Due = DateTime.Now + by;
        Sort();
        Save();
    }

    /// <summary>Done: a one-off disappears, a repeating one moves to its next time.</summary>
    public void Done(Reminder r)
    {
        if (r.Repeat == Repeat.Once)
        {
            Items.Remove(r);
        }
        else
        {
            var step = r.Repeat == Repeat.Daily ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);
            do r.Due += step; while (r.Due <= DateTime.Now);
        }
        Sort();
        Save();
    }

    public void Delete(Reminder r)
    {
        Items.Remove(r);
        Save();
    }

    private void Sort() => Items.Sort((a, b) => a.Due.CompareTo(b.Due));

    private void Save()
    {
        Store.Save(FileName, Items);
        Changed?.Invoke();
    }
}
