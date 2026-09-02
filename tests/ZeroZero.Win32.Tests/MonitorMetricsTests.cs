using Xunit;

namespace ZeroZero.Win32.Tests;

/// <summary>
/// Against the real operating system: every call reaches user32 or shcore, so an import whose
/// entry point does not resolve, or whose structure disagrees with the header, fails here.
/// </summary>
public class MonitorMetricsTests
{
    [Fact]
    public void PrimaryWorkArea_IsNotEmpty()
    {
        var area = MonitorMetrics.PrimaryWorkArea();

        Assert.True(area.Width > 0, $"width {area.Width}");
        Assert.True(area.Height > 0, $"height {area.Height}");
    }

    [Fact]
    public void ForCursor_ReportsANonEmptyWorkAreaAndAScaleOfAtLeastOne()
    {
        var (area, scale) = MonitorMetrics.ForCursor();

        Assert.True(area.Width > 0, $"width {area.Width}");
        Assert.True(area.Height > 0, $"height {area.Height}");
        // Windows scales up from 100%, never down.
        Assert.True(scale >= 1.0, $"scale {scale}");
    }

    [Fact]
    public void ScaleForWindow_IsOneForAHandleThatIsNotAWindow()
    {
        Assert.Equal(1.0, MonitorMetrics.ScaleForWindow(IntPtr.Zero));
    }

    [Fact]
    public void ScaleForWindow_IsTheWindowsDpiOverTheBaselineOfNinetySix()
    {
        using var window = new FramedWindow();
        uint dpi = FramedWindow.GetDpiForWindow(window.Handle);
        Assert.NotEqual(0u, dpi);

        Assert.Equal(dpi / 96.0, MonitorMetrics.ScaleForWindow(window.Handle));
    }

    [Fact]
    public void NonClientSize_IsZeroForAHandleThatIsNotAWindow()
    {
        Assert.Equal((0, 0), MonitorMetrics.NonClientSize(IntPtr.Zero));
    }

    [Fact]
    public void NonClientSize_OfTheDesktopWindow_IsZero()
    {
        // The desktop has no frame at all, so its outer and client rectangles coincide.
        Assert.Equal((0, 0), MonitorMetrics.NonClientSize(FramedWindow.GetDesktopWindow()));
    }

    [Fact]
    public void NonClientSize_OfAFramedWindow_HasBordersAndATallerTitleBar()
    {
        using var window = new FramedWindow();

        var (width, height) = MonitorMetrics.NonClientSize(window.Handle);

        // Borders on both sides make the width; the title bar on top of a border makes the height
        // the larger of the two.
        Assert.True(width > 0, $"width {width}");
        Assert.True(height > width, $"height {height} against width {width}");
    }
}
