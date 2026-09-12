using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using ZeroZero.Win32;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// What the About window's own sizing arithmetic arrives at, beside what the window actually got.
/// The height a measure reports and the height on screen are separate numbers, and only the second
/// one decides whether a row is inside the client area; reading the sizing code predicts neither.
/// Every figure is written as one tab-separated row so a run can be compared against another run.
/// </summary>
internal static class AboutProbe
{
    /// <summary>The width the About window lays its content out at, in device-independent units.</summary>
    private const double ContentWidth = 320;

    public static void Dump(string path, string label, Window window)
    {
        var lines = new List<string>();
        var root = (FrameworkElement)window.Content;
        IntPtr handle = Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        double scale = MonitorMetrics.ScaleForWindow(handle);
        var (ncWidth, ncHeight) = MonitorMetrics.NonClientSize(handle);

        // Three measures in a row, because the difference between them is the whole question: the
        // first repeats the call the window's own resize makes, the second forces the pass, and the
        // third reads what the live layout settled on.
        double cached = root.DesiredSize.Height;
        root.Measure(new Size(ContentWidth, double.PositiveInfinity));
        double remeasured = root.DesiredSize.Height;
        root.InvalidateMeasure();
        root.Measure(new Size(ContentWidth, double.PositiveInfinity));
        double invalidated = root.DesiredSize.Height;
        root.UpdateLayout();

        var size = window.AppWindow.Size;
        double clientHeight = (size.Height - ncHeight) / scale;

        lines.Add($"scale\t{scale:0.###}");

        // The direct statement of the question "does this have to be scrolled to": anything above
        // zero means the content is taller than the window gives it.
        foreach (var viewer in Scrollers(root))
        {
            lines.Add($"scroller\textent={viewer.ExtentHeight:0.##}\tviewport={viewer.ViewportHeight:0.##}" +
                      $"\tscrollable={viewer.ScrollableHeight:0.##}");
        }

        // The control's own height at two widths, because a host that measures at one width and lays
        // out at another gets a height the layout never has.
        if (Find<BrandAboutControl>(root, "AboutControl") is { } about)
        {
            lines.Add($"about-actual\twidth={about.ActualWidth:0.##}\theight={about.ActualHeight:0.##}");
            foreach (double width in new[] { 320.0, 480.0, 640.0 })
            {
                about.InvalidateMeasure();
                about.Measure(new Size(width, double.PositiveInfinity));
                lines.Add($"about-measured-at\t{width:0}\t{about.DesiredSize.Height:0.##}");
            }
            about.InvalidateMeasure();
            root.UpdateLayout();
        }
        lines.Add($"desired-height-cached\t{cached:0.##}");
        lines.Add($"desired-height-remeasured\t{remeasured:0.##}");
        lines.Add($"desired-height-after-invalidate\t{invalidated:0.##}");
        lines.Add($"root-actual-height\t{root.ActualHeight:0.##}");
        lines.Add($"appwindow-size\t{size.Width}x{size.Height}");
        lines.Add($"non-client\t{ncWidth}x{ncHeight}");
        lines.Add($"client-height-dip\t{clientHeight:0.##}");

        foreach (var (name, element) in Named(root))
        {
            try
            {
                var point = element.TransformToVisual(root).TransformPoint(new Point(0, 0));
                double bottom = point.Y + element.ActualHeight;
                lines.Add($"element\t{name}\ttop={point.Y:0.##}\theight={element.ActualHeight:0.##}" +
                          $"\tbottom={bottom:0.##}\tvisible={element.Visibility}" +
                          $"\tinside-client={(bottom <= clientHeight ? "yes" : "NO")}\t{Says(element)}");
            }
            catch (Exception ex)
            {
                lines.Add($"element\t{name}\tno-transform\t{ex.GetType().Name}");
            }
        }

        File.AppendAllLines(path, lines.Select(line => $"{label}\t{line}"));
    }

    /// <summary>Every scroll viewer in the tree, in the order the walk meets them.</summary>
    private static List<ScrollViewer> Scrollers(DependencyObject node)
    {
        var found = new List<ScrollViewer>();
        CollectScrollers(node, found);
        return found;
    }

    private static void CollectScrollers(DependencyObject node, List<ScrollViewer> found)
    {
        if (node is ScrollViewer viewer) found.Add(viewer);

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++) CollectScrollers(VisualTreeHelper.GetChild(node, i), found);
    }

    /// <summary>What an element says, where it says anything — a row's position is worth little
    /// without the words that make it recognisable.</summary>
    private static string Says(FrameworkElement element) => element switch
    {
        TextBlock text => Trim(text.Text),
        Button { Content: string content } => Trim(content),
        _ => "",
    };

    private static string Trim(string? text)
    {
        text = (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        return text.Length > 80 ? text[..80] : text;
    }

    /// <summary>Every named element in the tree, so a row is found by the name the markup gave it.</summary>
    private static List<(string Name, FrameworkElement Element)> Named(DependencyObject node)
    {
        var found = new List<(string, FrameworkElement)>();
        Collect(node, found);
        return found;
    }

    private static void Collect(DependencyObject node, List<(string, FrameworkElement)> found)
    {
        if (node is FrameworkElement { Name.Length: > 0 } element) found.Add((element.Name, element));

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++) Collect(VisualTreeHelper.GetChild(node, i), found);
    }

    /// <summary>The first element in the tree carrying this name, for a scripted click.</summary>
    public static T? Find<T>(DependencyObject node, string name) where T : FrameworkElement
    {
        if (node is T match && match.Name == name) return match;

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (Find<T>(VisualTreeHelper.GetChild(node, i), name) is { } found) return found;
        }
        return null;
    }
}
