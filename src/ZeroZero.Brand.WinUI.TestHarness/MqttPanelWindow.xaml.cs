using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using ZeroZero.Win32;

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
        string title, ElementTheme theme, bool broker, bool publish, bool edited, int offset,
        bool mica = false, bool invalidPort = false)
    {
        InitializeComponent();
        Title = title;
        Root.RequestedTheme = theme;

        if (mica)
        {
            // The ground a settings page most often has and a rig least often reproduces: the page
            // paints nothing, so every translucent surface on the panel composites over the
            // backdrop rather than over a flat colour.
            Root.Background = null;
            SystemBackdrop = new MicaBackdrop();
        }

        Panel.Initialise(MqttPanelSample.Build());
        Panel.BrokerExpanded = broker;
        Panel.PublishExpanded = publish;
        if (edited) Panel.Loaded += (_, _) => StageAnEdit();
        if (invalidPort) Panel.Loaded += (_, _) => StageInvalidPort();

        Resize(offset);
    }

    /// <summary>The realised root, for the theme probe. A rendered tree is the only place the
    /// brush that actually reached an element can be read.</summary>
    internal FrameworkElement ProbeRoot => Root;

    /// <summary>Opens the device-id dialogue through its own button, so a capture can reach the
    /// text tiers that only exist inside it.</summary>
    internal void OpenDeviceIdDialogue()
    {
        string report = Path.Combine(Path.GetTempPath(), "mqtt-harness-dialogue.txt");
        try
        {
            if (FindDescendant(Panel, "ChangeDeviceIdBtn") is Button button &&
                FrameworkElementAutomationPeer.CreatePeerForElement(button) is IInvokeProvider invoke)
                invoke.Invoke();
            else
                File.WriteAllText(report, "Change-ID button or its invoke provider not found.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(report, ex.ToString());
        }
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

    /// <summary>
    /// Types a port outside the valid range, so the panel's error tier is on screen. The error
    /// colour is the one tier no ordinary screenshot reaches, and the one nearest the contrast floor.
    /// </summary>
    private void StageInvalidPort()
    {
        Panel.BrokerExpanded = true;
        Panel.UpdateLayout();

        // The custom entry is the last one; the typed box only appears once it is selected.
        if (FindDescendant(Panel, "PortCombo") is ComboBox { Items.Count: > 0 } combo)
            combo.SelectedIndex = combo.Items.Count - 1;
        Panel.UpdateLayout();

        if (FindDescendant(Panel, "PortCustomBox") is TextBox box) box.Text = "70000";
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
        double scale = MonitorMetrics.ScaleForWindow(window);
        var (chromeWidth, chromeHeight) = MonitorMetrics.NonClientSize(window);

        int workAreaHeight = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)
                                        .WorkArea.Height;
        int wanted = (int)Math.Ceiling(contentHeight * scale) + chromeHeight;
        int height = Math.Min(wanted, (int)(workAreaHeight * MaxWorkAreaFraction));

        AppWindow.Resize(new SizeInt32((int)Math.Ceiling(ClientWidth * scale) + chromeWidth, height));
        // Cascaded rather than stacked, so each window's title bar stays reachable and the capture
        // script can bring one forward at a time.
        AppWindow.Move(new PointInt32(offset, 0));
    }
}
