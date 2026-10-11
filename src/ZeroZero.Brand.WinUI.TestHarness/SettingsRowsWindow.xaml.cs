using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroZero.Controls.WinUI;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// The settings-row vocabulary on screen: section headers and rows in every shape a page uses, in
/// a plain window with ordinary chrome and a scroll viewer, the way a host's settings page gives
/// them. One window per theme and per width, so light and dark, and a page wide enough for the
/// field column and one too narrow for it, can each be looked at rather than inferred.
/// </summary>
public sealed partial class SettingsRowsWindow : Window
{
    /// <summary>How much of the work area a window may take; the rest scrolls.</summary>
    private const double MaxWorkAreaFraction = 0.94;

    public SettingsRowsWindow(string title, ElementTheme theme, double clientWidth, int offset)
    {
        InitializeComponent();
        Title = title;
        Root.RequestedTheme = theme;

        // The code path a panel takes for rows it declares at run time: the same properties,
        // assigned rather than written in markup.
        Page.Children.Add(new SettingsRow
        {
            Header = "Built in code",
            Description = "Header, description, bubble and field assigned from code.",
            Info = "A row a panel builds from a declared group binds these same properties.",
            Field = new ToggleSwitch { OnContent = "On", OffContent = "Off" },
        });

        Resize(clientWidth, offset);
    }

    /// <summary>The realised root, for the theme probe.</summary>
    internal FrameworkElement ProbeRoot => Root;

    private void Resize(double clientWidth, int offset) =>
        WindowSizing.FitToContent(this, Scroller, clientWidth, offset, MaxWorkAreaFraction);
}
