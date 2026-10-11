using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// The brand resource dictionary on screen: one swatch per brush key, a strip putting black and
/// white text on three accents — steel blue, terracotta and Indigo — and a 24 % tint of each on the
/// brand ground, the wordmark on two colour keys, and a sample line in the brand face, every one
/// resolved through ThemeResource the way a consuming application resolves them. Indigo is there
/// because it is the accent both rules run the other way on. One window per theme, so light and
/// dark sit side by side.
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
                lines.Add($"{Title}\tWordmark stop {stop.Offset:0}\t{ThemeProbe.Hex(stop.Color)}");

        lines.Add($"{Title}\tBrandFontFamily\t{Sample.FontFamily.Source}");
        File.AppendAllLines(path, lines);
    }

    private static string Describe(Brush? brush) =>
        brush is SolidColorBrush solid ? ThemeProbe.Hex(solid.Color) : brush is null ? "none" : brush.GetType().Name;

    private void Resize(int offset) =>
        WindowSizing.FitToContent(this, Body, ClientWidth, offset);
}
