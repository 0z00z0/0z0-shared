using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using ZeroZero.Win32;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Sizes a rig window to its content: measured at the width it will be displayed at, because a
/// narrower measure wraps text into lines the rendered layout never needs, then converted at the
/// window's own scaling with its non-client frame added, so the surplus is not empty space.
/// </summary>
internal static class WindowSizing
{
    /// <param name="window">The window to resize.</param>
    /// <param name="content">The element whose height decides the window's.</param>
    /// <param name="clientWidth">Client width in device-independent units.</param>
    /// <param name="offset">Cascade position from the left and top of the screen; null leaves the window where it is.</param>
    /// <param name="heightFraction">The share of the primary work area the window may take before the content scrolls; null for no cap.</param>
    /// <param name="minimumContentHeight">A floor for the content height, in device-independent units.</param>
    public static void FitToContent(Window window, FrameworkElement content, double clientWidth,
                                    int? offset = null, double? heightFraction = null,
                                    double minimumContentHeight = 0)
    {
        content.Measure(new Windows.Foundation.Size(clientWidth, double.PositiveInfinity));
        double contentHeight = Math.Max(content.DesiredSize.Height, minimumContentHeight);

        IntPtr handle = Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        double scale = MonitorMetrics.ScaleForWindow(handle);
        var (chromeWidth, chromeHeight) = MonitorMetrics.NonClientSize(handle);

        int height = (int)Math.Ceiling(contentHeight * scale) + chromeHeight;
        if (heightFraction is { } fraction)
        {
            int workAreaHeight = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary)
                                            .WorkArea.Height;
            height = Math.Min(height, WindowFit.HeightCap(workAreaHeight, fraction));
        }

        window.AppWindow.Resize(new SizeInt32((int)Math.Ceiling(clientWidth * scale) + chromeWidth, height));
        if (offset is { } left) window.AppWindow.Move(new PointInt32(left, 0));
    }
}
