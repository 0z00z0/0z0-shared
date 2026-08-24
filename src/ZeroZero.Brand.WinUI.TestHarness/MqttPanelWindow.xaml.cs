using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Hosts <see cref="ZeroZero.Mqtt.WinUI.MqttSettingsPanel"/> in a plain window with ordinary chrome
/// and a scroll viewer — the shape a full windowed application's settings page gives it. One window
/// per theme and per expander state, so light and dark, and collapsed and open, can each be looked at
/// on screen rather than inferred from a build.
/// </summary>
public sealed partial class MqttPanelWindow : Window
{
    /// <summary>Client width in device-independent units. Wide enough for the panel's own field
    /// column plus a settings page's margins, and no wider: the point is the panel's real proportions.</summary>
    private const double ClientWidth = 780;

    /// <summary>How much of the work area a window may take. The panel with both groups open is
    /// taller than most displays, so the rig scrolls rather than opening a window nothing can
    /// composite.</summary>
    private const double MaxWorkAreaFraction = 0.94;

    public MqttPanelWindow(
        string title, ElementTheme theme, bool broker, bool publish, bool edited, int offset)
    {
        InitializeComponent();
        Title = title;
        Root.RequestedTheme = theme;

        Panel.Initialise(MqttPanelSample.Build());
        Panel.BrokerExpanded = broker;
        Panel.PublishExpanded = publish;
        if (edited) Panel.Loaded += (_, _) => StageAnEdit();

        Resize(offset);
    }

    /// <summary>
    /// Edits the panel's own host box, so the unapplied marker can be seen while the group holding
    /// the field is closed — the state a staged edit used to be lost in. The rig reaches into the
    /// realised tree rather than the panel growing an entry point for it: nothing an application
    /// would call belongs on the panel for a screenshot's sake.
    /// </summary>
    /// <remarks>The field is cleared rather than retyped, because any other value would settle into
    /// a probe and this rig touches no network at all. The group is opened and closed again around
    /// the edit: a collapsed expander never realises its rows, so there is nothing in the tree to
    /// type into until it does.</remarks>
    private void StageAnEdit()
    {
        bool wasExpanded = Panel.BrokerExpanded;
        Panel.BrokerExpanded = true;
        Panel.UpdateLayout();

        if (FindDescendant(Panel, "HostBox") is TextBox host) host.Text = "";

        Panel.BrokerExpanded = wasExpanded;
    }

    /// <summary>The first descendant carrying a given name. The compiled-XAML namescope is not
    /// reachable from outside the control, but the realised tree is, and every named element keeps
    /// its name on the element itself.</summary>
    private static FrameworkElement? FindDescendant(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { } element && element.Name == name) return element;
            if (FindDescendant(child, name) is { } found) return found;
        }
        return null;
    }

    private void Resize(int offset)
    {
        // Measure the content at the width it will be displayed at: measuring narrower wraps the
        // descriptions into extra lines and reports a height the rendered layout never needs.
        Scroller.Measure(new Windows.Foundation.Size(ClientWidth, double.PositiveInfinity));
        double contentHeight = Scroller.DesiredSize.Height;

        IntPtr window = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        double scale = NativeMethods.GetScaleForWindow(window);
        SizeInt32 chrome = NativeMethods.GetChromeSizeForWindow(window);

        int workAreaHeight = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)
                                        .WorkArea.Height;
        int wanted = (int)Math.Ceiling(contentHeight * scale) + chrome.Height;
        int height = Math.Min(wanted, (int)(workAreaHeight * MaxWorkAreaFraction));

        AppWindow.Resize(new SizeInt32((int)Math.Ceiling(ClientWidth * scale) + chrome.Width, height));
        // Cascaded rather than stacked, so each window's title bar stays reachable and the capture
        // script can bring one forward at a time.
        AppWindow.Move(new PointInt32(offset, 0));
    }
}
