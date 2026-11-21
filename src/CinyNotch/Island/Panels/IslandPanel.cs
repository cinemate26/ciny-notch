using Avalonia.Controls;
using CinyNotch.Services;

namespace CinyNotch.Island.Panels;

/// <summary>What a panel can ask of the island it lives in.</summary>
internal interface IIslandHost
{
    /// <summary>Lets the island take keyboard focus, for typing.</summary>
    void EnableKeyboard();
    void ShowAlert(Alert alert);
    void SelectTab(string name);
    /// <summary>Ask the island to recompute its size (a panel's height changed).</summary>
    void PanelResized();
    void Close();
}

internal sealed record AlertButton(string Text, Action OnClick, bool Primary = false);

internal sealed record Alert(string Glyph, string Title, string Subtitle, IReadOnlyList<AlertButton> Buttons,
    TimeSpan ShowFor, string GlyphColor = Ui.Ui.Primary, bool Beep = false, Avalonia.Media.Imaging.Bitmap? Logo = null);

/// <summary>One tab of the island's card.</summary>
internal abstract class IslandPanel : UserControl
{
    protected IslandPanel(Hub hub, IIslandHost host)
    {
        Hub = hub;
        Host = host;
    }

    protected Hub Hub { get; }
    protected IIslandHost Host { get; }

    public abstract string TabName { get; }

    /// <summary>Card height while this tab is open (the width is always 560).</summary>
    public virtual double CardHeight => 200;

    /// <summary>The tab just became visible on an open island.</summary>
    public virtual void OnShown() { }

    /// <summary>Called about four times a second while this tab is visible.</summary>
    public virtual void OnTick() { }
}
