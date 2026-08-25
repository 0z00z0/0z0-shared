using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Walks a rendered tree and records, for every text run and every control part, the brush that
/// actually reached it, the opacity chain above it, the ground it sits on, and the resulting
/// contrast ratio. Reading a resource dictionary predicts none of this: a control template resolves
/// its own keys, and a style setter and a visual state can land on the same property.
/// </summary>
internal static class ThemeProbe
{
    private const double MinimumRatio = 4.5;

    public static void Dump(string path, string label, FrameworkElement root)
    {
        var lines = new List<string>();
        Collect(root, lines);
        // A dialogue is rooted beside the page rather than inside it, so a walk from the window's
        // own content never reaches one. Its tiers are read from the capture instead: walking an
        // open popup's tree from here does not return.
        File.AppendAllLines(path, lines.Select(l => $"{label}\t{l}"));
    }

    private static void Collect(DependencyObject node, List<string> lines)
    {
        switch (node)
        {
            case TextBlock text when text.Text.Length > 0:
                lines.Add(Record("TextBlock", Describe(text.Text), text, text.Foreground, text.FontSize));
                break;
            case ComboBox combo:
                lines.Add(Record("ComboBox.Foreground", Describe($"{combo.Name}={combo.SelectedItem}"), combo, combo.Foreground, combo.FontSize));
                lines.Add(Record("ComboBox.Background", Describe(combo.Name), combo, combo.Background, combo.FontSize));
                break;
            case TextBox box:
                lines.Add(Record("TextBox.Foreground", Describe($"{box.Name}={box.Text}"), box, box.Foreground, box.FontSize));
                lines.Add(Record("TextBox.Background", Describe(box.Name), box, box.Background, box.FontSize));
                break;
            case PasswordBox pwd:
                lines.Add(Record("PasswordBox.Foreground", Describe(pwd.Name), pwd, pwd.Foreground, pwd.FontSize));
                lines.Add(Record("PasswordBox.Background", Describe(pwd.Name), pwd, pwd.Background, pwd.FontSize));
                break;
            case Button button:
                lines.Add(Record("Button.Foreground", Describe(button.Name), button, button.Foreground, button.FontSize));
                lines.Add(Record("Button.Background", Describe(button.Name), button, button.Background, button.FontSize));
                break;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++) Collect(VisualTreeHelper.GetChild(node, i), lines);
    }

    /// <summary>One tab-separated row: what it is, what it says, where it is, the colour that
    /// reached it, the ground beneath it and the ratio between the two.</summary>
    private static string Record(string kind, string what, FrameworkElement element, Brush? brush, double fontSize)
    {
        Color? own = ColourOf(brush);
        double opacity = OpacityChain(element);
        Color ground = Ground(element);

        string x = "-", y = "-";
        try
        {
            var point = element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            x = ((int)Math.Round(point.X)).ToString();
            y = ((int)Math.Round(point.Y)).ToString();
        }
        catch (Exception)
        {
            // An element outside a live tree has no transform; position is a convenience here.
        }

        if (own is not { } colour)
            return $"{kind}\t{what}\t{x}\t{y}\t{fontSize:0.#}\tnull\t{Hex(ground)}\t-\t-";

        Color effective = Over(WithOpacity(colour, opacity), ground);
        double ratio = Contrast(effective, ground);
        string verdict = kind.EndsWith("Background", StringComparison.Ordinal) ? "-"
            : ratio < MinimumRatio ? "FAIL" : "pass";

        return $"{kind}\t{what}\t{x}\t{y}\t{fontSize:0.#}\t{Hex(colour)}@{opacity:0.###}\t{Hex(ground)}\t{Hex(effective)}\t{ratio:0.00}\t{verdict}";
    }

    private static string Describe(string? text)
    {
        text = (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        return text.Length > 44 ? text[..44] : text;
    }

    private static Color? ColourOf(Brush? brush) => brush switch
    {
        SolidColorBrush solid => solid.Color with { A = (byte)Math.Round(solid.Color.A * solid.Opacity) },
        // An acrylic or gradient ground is not a single value; the capture is the authority there.
        null => null,
        _ => null,
    };

    private static double OpacityChain(DependencyObject? node)
    {
        double opacity = 1.0;
        while (node is not null)
        {
            if (node is UIElement element) opacity *= element.Opacity;
            node = VisualTreeHelper.GetParent(node);
        }
        return opacity;
    }

    /// <summary>Composites every background above the element until one is opaque — a card ground
    /// is itself translucent over the page, so the colour a reader sees is the stack, not the card.</summary>
    private static Color Ground(DependencyObject? node)
    {
        var stack = new List<Color>();
        node = VisualTreeHelper.GetParent(node);
        while (node is not null)
        {
            Brush? background = node switch
            {
                Panel panel => panel.Background,
                Border border => border.Background,
                ContentPresenter presenter => presenter.Background,
                Control control => control.Background,
                _ => null,
            };
            if (ColourOf(background) is { A: > 0 } colour)
            {
                double opacity = node is UIElement e ? e.Opacity : 1.0;
                stack.Add(WithOpacity(colour, opacity));
                if (colour.A == 255 && opacity >= 1.0) break;
            }
            node = VisualTreeHelper.GetParent(node);
        }

        // Bottom-up: the last one found is furthest back.
        Color ground = Color.FromArgb(255, 0, 0, 0);
        for (int i = stack.Count - 1; i >= 0; i--) ground = Over(stack[i], ground);
        return ground;
    }

    private static Color WithOpacity(Color colour, double opacity) =>
        colour with { A = (byte)Math.Clamp(Math.Round(colour.A * opacity), 0, 255) };

    private static Color Over(Color top, Color bottom)
    {
        double a = top.A / 255.0;
        return Color.FromArgb(
            255,
            (byte)Math.Round(top.R * a + bottom.R * (1 - a)),
            (byte)Math.Round(top.G * a + bottom.G * (1 - a)),
            (byte)Math.Round(top.B * a + bottom.B * (1 - a)));
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c) =>
        0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte value)
    {
        double v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
