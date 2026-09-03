using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// Everything the settings window takes from the application: the sections and their pages,
/// the product identity for the pane footer, where the rectangle is kept, the theme, and the
/// few measurements the two applications choose differently. The window never reaches past it.
/// </summary>
public sealed class SettingsWindowSetup
{
    /// <summary>The window title.</summary>
    public required string Title { get; init; }

    /// <summary>The sections, in pane order. At least one; tags unique.</summary>
    public required IReadOnlyList<SettingsSection> Sections { get; init; }

    /// <summary>The section shown first. The first declared when left null.</summary>
    public string? InitialTag { get; init; }

    /// <summary>The theme the window renders in, title bar included. Default follows the
    /// application; an application pinned to one theme passes it here.</summary>
    public ElementTheme Theme { get; init; } = ElementTheme.Default;

    /// <summary>Where the rectangle is kept between runs. None opens centred on the cursor's
    /// monitor every time and remembers nothing.</summary>
    public IWindowRectStore? RectStore { get; init; }

    /// <summary>Client width in device-independent units when nothing is saved.</summary>
    public double DefaultClientWidth { get; init; } = 960;

    /// <summary>Client height in device-independent units when nothing is saved.</summary>
    public double DefaultClientHeight { get; init; } = 640;

    /// <summary>The navigation pane's width in device-independent units.</summary>
    public double PaneWidth { get; init; } = 224;

    /// <summary>The most a page may be wide, in device-independent units, held to the left edge
    /// when the window is wider. Unbounded by default: a page fills the window.</summary>
    public double PageMaxWidth { get; init; } = double.PositiveInfinity;

    /// <summary>The space between the pane, the edges and the page.</summary>
    public Thickness PagePadding { get; init; } = new(24, 20, 24, 24);

    /// <summary>The product mark in the pane footer, 28 units square. None shows no mark.</summary>
    public ImageSource? ProductMark { get; init; }

    /// <summary>The product name in the pane footer.</summary>
    public string ProductName { get; init; } = "";

    /// <summary>The version beneath the name in the pane footer.</summary>
    public string ProductVersion { get; init; } = "";
}
