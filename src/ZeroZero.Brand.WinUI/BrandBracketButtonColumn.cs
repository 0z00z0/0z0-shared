using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// Stacks <see cref="BrandBracketButton"/>s one per line and gives them all the width of the widest,
/// so their brackets line up into a column instead of sitting at three different lengths. The column
/// itself is only as wide as that widest button, so a host centres or aligns the whole group as one
/// thing.
/// </summary>
/// <remarks>
/// A collapsed button takes no width, no height and no spacing, so a column whose buttons change
/// with what is on screen closes up rather than leaving a gap — and the width follows the buttons
/// actually showing.
/// <para>
/// Each button is told to fill the width it is given; a button placed anywhere else is unchanged
/// and still sizes itself to its own text.
/// </para>
/// </remarks>
public sealed partial class BrandBracketButtonColumn : Panel
{
    private double _spacing = 2;

    /// <summary>The gap between neighbouring buttons.</summary>
    public double Spacing
    {
        get => _spacing;
        set
        {
            if (_spacing == value) return;
            _spacing = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Two passes. The first asks each button what its own text wants; the second lays every one
        // out at the widest of those answers, which is the width they are arranged at.
        double widest = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            if (child is BrandBracketButton button) button.FillsWidth = true;

            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            widest = Math.Max(widest, child.DesiredSize.Width);
        }

        // Never wider than the room there is: a long label wraps the column's problem onto the host
        // rather than overflowing it.
        if (widest > availableSize.Width) widest = availableSize.Width;

        double height = 0;
        int shown = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            child.Measure(new Size(widest, double.PositiveInfinity));
            height += child.DesiredSize.Height + (shown > 0 ? Spacing : 0);
            shown++;
        }

        return new Size(widest, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        int shown = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            if (shown > 0) y += Spacing;
            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
            shown++;
        }

        return finalSize;
    }
}
