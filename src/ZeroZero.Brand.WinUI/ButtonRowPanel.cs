using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// Lays the About row's buttons out left to right at their natural widths and starts a new line
/// when the next one does not fit. A collapsed button takes no width and adds no spacing, which a
/// grid of fixed columns cannot do.
/// </summary>
internal sealed partial class ButtonRowPanel : Panel
{
    /// <summary>The gap between neighbouring buttons, and between wrapped lines.</summary>
    public double Spacing { get; init; }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0, lineWidth = 0, lineHeight = 0;
        int onLine = 0;

        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;

            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;

            if (onLine > 0 && lineWidth + Spacing + size.Width > availableSize.Width)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight + Spacing;
                lineWidth = 0;
                lineHeight = 0;
                onLine = 0;
            }

            lineWidth += (onLine > 0 ? Spacing : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            onLine++;
        }

        return new Size(Math.Max(width, lineWidth), height + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        int onLine = 0;

        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;

            var size = child.DesiredSize;

            // The same break rule as the measure pass, so the lines arranged are the lines measured.
            if (onLine > 0 && x + Spacing + size.Width > finalSize.Width)
            {
                y += lineHeight + Spacing;
                x = 0;
                lineHeight = 0;
                onLine = 0;
            }

            if (onLine > 0) x += Spacing;
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            onLine++;
        }

        return finalSize;
    }
}
