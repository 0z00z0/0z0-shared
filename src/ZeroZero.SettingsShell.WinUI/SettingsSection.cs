using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// One entry in the navigation pane and the page behind it. The page is the application's
/// entirely: the shell calls <see cref="Build"/> for it, shows and hides it, and calls the two
/// hooks around every change of section; it reads nothing on the page and imposes no layout on
/// it beyond the scroll viewer it sits in.
/// </summary>
public sealed class SettingsSection
{
    /// <summary>The section's identity: what <see cref="SettingsWindow.Navigate"/> and
    /// <see cref="SettingsWindow.Rebuild(string)"/> name it by. Unique within a window.</summary>
    public required string Tag { get; init; }

    /// <summary>The text in the pane.</summary>
    public required string Label { get; init; }

    /// <summary>The icon beside the label, drawn in a 28-unit box. A font glyph, a bitmap or an
    /// SVG, as the application likes; none shows no icon.</summary>
    public IconSource? Icon { get; init; }

    /// <summary>
    /// Builds the page. Called once as the window opens, and again on a rebuild unless
    /// <see cref="BuildOnce"/> is set. The page need not be finished when it returns — content that
    /// arrives later fills a container built here — and everything conditional about it, on
    /// hardware or on state, is decided in here, before any measure of it.
    /// </summary>
    public required Func<UIElement> Build { get; init; }

    /// <summary>Runs after the page is on screen: start a timer, re-read what a live source
    /// decides. Also runs after a rebuild of the current section, on the new page.</summary>
    public Action? Enter { get; init; }

    /// <summary>Runs before the page leaves the screen — on a change of section, on a rebuild of
    /// this one, and when the window closes while it is current. Stop what <see cref="Enter"/>
    /// started; the page itself stays as it is.</summary>
    public Action? Leave { get; init; }

    /// <summary>
    /// Built once for the life of the window: <see cref="SettingsWindow.Rebuild()"/> leaves it
    /// alone, and <see cref="SettingsWindow.Rebuild(string)"/> naming it is refused. For a page
    /// that may be initialised once and holds state a rebuild would lose — a panel with a staged,
    /// unapplied edit.
    /// </summary>
    public bool BuildOnce { get; init; }
}
