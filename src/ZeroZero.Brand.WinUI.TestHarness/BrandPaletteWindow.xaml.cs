using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using ZeroZero.Win32;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// The brand resource dictionary on screen: one swatch per brush key, the wordmark on two colour
/// keys, and a sample line in the brand face, every one resolved through ThemeResource the way a
/// consuming application resolves them. One window per theme, so light and dark sit side by side.
/// </summary>
public sealed partial class BrandPaletteWindow : Window
{
    /// <summary>Client width in device-independent units; the swatch column plus its labels.</summary>
    private const double ClientWidth = 420;

    public BrandPaletteWindow(string title, ElementTheme theme, int offset)
    {
        InitializeComponent();
        Title = title;
        Root.RequestedTheme = theme;
        Resize(offset);
    }

    /// <summary>
    /// Appends the colour that reached each swatch and each gradient stop, and the face that reached
    /// the sample line. The resolved values are the only place a key that missed shows up: a swatch
    /// with no brush is a gap on screen, and a gap is easy to read as a margin.
    /// </summary>
    internal void Probe(string path)
    {
        var lines = new List<string>();
        foreach (var child in Swatches.Children)
        {
            if (child is not Grid row) continue;
            var swatch = row.Children.OfType<Border>().First();
            var label = row.Children.OfType<TextBlock>().First();
            lines.Add($"{Title}\t{label.Text}\t{Describe(swatch.Background)}");
        }

        if (Wordmark.Foreground is LinearGradientBrush gradient)
            foreach (var stop in gradient.GradientStops)
                lines.Add($"{Title}\tWordmark stop {stop.Offset:0}\t{Hex(stop.Color)}");

        lines.Add($"{Title}\tBrandFontFamily\t{Sample.FontFamily.Source}");
        File.AppendAllLines(path, lines);
    }

    private static string Describe(Brush? brush) =>
        brush is SolidColorBrush solid ? Hex(solid.Color) : brush is null ? "none" : brush.GetType().Name;

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private void Resize(int offset)
    {
        Body.Measure(new Windows.Foundation.Size(ClientWidth, double.PositiveInfinity));
        double contentHeight = Body.DesiredSize.Height;

        IntPtr window = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        double scale = MonitorMetrics.ScaleForWindow(window);
        var (chromeWidth, chromeHeight) = MonitorMetrics.NonClientSize(window);

        AppWindow.Resize(new SizeInt32(
            (int)Math.Ceiling(ClientWidth * scale) + chromeWidth,
            (int)Math.Ceiling(contentHeight * scale) + chromeHeight));
        AppWindow.Move(new PointInt32(offset, 0));
    }
}
