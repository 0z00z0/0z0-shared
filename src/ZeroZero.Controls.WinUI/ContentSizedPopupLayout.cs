namespace ZeroZero.Controls.WinUI;

/// <summary>The framework-free arithmetic of <c>ContentSizedPopup</c>, in physical pixels.</summary>
internal static class ContentSizedPopupLayout
{
    /// <summary>Used while a measure still answers zero, so the window never opens at its frame alone.</summary>
    internal const double FallbackHeight = 200;

    /// <summary>Content rounded up (a pixel short clips the last row) plus the frame, capped at the work area.</summary>
    internal static (int Width, int Height) OuterSize(
        double contentWidth, double measuredHeight, double scale, int frameWidth, int frameHeight, int workHeight)
    {
        double height = measuredHeight > 0 ? measuredHeight : FallbackHeight;
        int outerWidth = (int)Math.Ceiling(contentWidth * scale) + frameWidth;
        int outerHeight = (int)Math.Ceiling(height * scale) + frameHeight;
        if (workHeight > 0 && outerHeight > workHeight) outerHeight = workHeight;
        return (outerWidth, outerHeight);
    }

    /// <summary>Centred in the work area; a window taller than it keeps its top on screen.</summary>
    internal static (int X, int Y) Origin(
        int workLeft, int workTop, int workWidth, int workHeight, int outerWidth, int outerHeight) =>
        (workLeft + (workWidth - outerWidth) / 2,
         workTop + Math.Max(0, (workHeight - outerHeight) / 2));
}
