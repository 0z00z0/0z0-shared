namespace ZeroZero.Win32;

/// <summary>
/// Window-placement arithmetic in physical pixels: given the rectangle a window wants, the height
/// its content needs and the work area it lands on, the rectangle it actually opens at. Plain
/// numbers, so a caller on any user-interface framework does the measuring and this does the
/// geometry.
/// </summary>
public static class WindowFit
{
    /// <summary>The share of a work area a window sized to its own content may grow to before the
    /// content scrolls instead. A caller with another policy passes its own fraction.</summary>
    public const double DefaultHeightFraction = 0.8;

    /// <summary>
    /// The rectangle to open at: grown to <paramref name="requiredHeight"/> where the content is
    /// taller, never wider or taller than <paramref name="workArea"/>, and wholly inside it.
    /// <para><paramref name="requiredHeight"/> is a window height rather than a content height —
    /// only the caller can measure the title bar and the frame. Zero clamps without growing.</para>
    /// <para>A rectangle that misses the work area entirely is re-centred rather than clamped:
    /// clamping alone jams it into the nearest corner, which is where a window restored onto a
    /// monitor that has gone would land.</para>
    /// </summary>
    public static NativeRect Fit(NativeRect desired, int requiredHeight, NativeRect workArea)
    {
        int width = Math.Min(desired.Width, workArea.Width);
        int height = Math.Min(Math.Max(desired.Height, requiredHeight), workArea.Height);

        // Half-open, as NativeRect is: a rectangle that merely touches the work area's edge shares
        // no pixel with it and counts as off-screen.
        bool onScreen = desired.Left < workArea.Right && desired.Right > workArea.Left
                     && desired.Top < workArea.Bottom && desired.Bottom > workArea.Top;

        if (!onScreen)
        {
            int left = workArea.Left + (workArea.Width - width) / 2;
            int top = workArea.Top + (workArea.Height - height) / 2;
            return new NativeRect(left, top, left + width, top + height);
        }

        return new NativeRect(desired.Left, desired.Top, desired.Left + width, desired.Top + height)
            .ClampInto(workArea);
    }

    /// <summary>
    /// The height a window must be for content of <paramref name="contentHeight"/> to show without
    /// scrolling, given that it is <paramref name="currentHeight"/> tall and shows
    /// <paramref name="viewportHeight"/> of that content today. The difference carries the title bar
    /// and the scroller's padding, so no chrome is added up by hand. Unlike <see cref="Fit"/> this
    /// may also shrink, down to <paramref name="minimumHeight"/>.
    /// <para>Unit-agnostic, and all four values must share a unit: mixing device-independent units
    /// with physical pixels mis-sizes the window on every scaled display.</para>
    /// </summary>
    public static int HeightForContent(double currentHeight, double contentHeight,
                                       double viewportHeight, int minimumHeight) =>
        Math.Max(minimumHeight, (int)Math.Ceiling(currentHeight + contentHeight - viewportHeight));

    /// <summary>The tallest a window sized to its own content may grow to, in the work area's own
    /// unit.</summary>
    public static int HeightCap(int workAreaHeight, double fraction) =>
        (int)(workAreaHeight * fraction);

    /// <summary>
    /// The height in physical pixels of a window showing <paramref name="contentUnits"/> of content
    /// with no scrolling: the content converted at <paramref name="scale"/>, plus the window's own
    /// non-client height, capped at the <see cref="HeightCap"/> of <paramref name="workAreaHeight"/>
    /// where the scroller takes over. The content's height is independent of the window's, so no
    /// layout pass at the final size is needed first.
    /// <para><paramref name="minimumContentUnits"/> wins over the cap. A window shorter than its
    /// own floor has no room for what it must always show, while one past the fraction is only
    /// taller than the policy prefers. <paramref name="workAreaHeight"/> is the single bound
    /// nothing passes, so a floor larger than the monitor gives a window the size of the work
    /// area and no more.</para>
    /// </summary>
    public static int ContentFittedHeight(double contentUnits, double scale, int chromeHeight,
                                          int workAreaHeight, int minimumContentUnits, double fraction)
    {
        int chrome = Math.Max(chromeHeight, 0);
        int floor = ToPhysicalPixels(minimumContentUnits, scale) + chrome;
        int needed = ToPhysicalPixels((int)Math.Ceiling(contentUnits), scale) + chrome;

        int capped = Math.Min(needed, HeightCap(workAreaHeight, fraction));
        return Math.Min(Math.Max(capped, floor), workAreaHeight);
    }

    /// <summary>
    /// Device-independent units to physical pixels. A window's native size is physical pixels while
    /// its layout is in device-independent ones, so a minimum passed through unscaled is 43 % short
    /// on a 175 % panel.
    /// <para>The scale belongs to the window's own display. A process-wide reading is 96 dpi from a
    /// process that is not per-monitor aware, which reads as 100 % everywhere and quietly disables
    /// the scaling. Anything at or below zero is treated as 100 %.</para>
    /// </summary>
    public static int ToPhysicalPixels(int units, double scale) =>
        (int)Math.Ceiling(units * (scale > 0 ? scale : 1.0));
}
