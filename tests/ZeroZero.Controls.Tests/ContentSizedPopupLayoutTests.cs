using Xunit;
using ZeroZero.Controls.WinUI;

namespace ZeroZero.Controls.Tests;

public class ContentSizedPopupLayoutTests
{
    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData(100.1, 1.75, 1000, 186)]   // 175.175 rounds up to 176, so the last row is not clipped
    [InlineData(0, 1.5, 1000, 310)]        // a zero measure takes the fallback height
    [InlineData(900, 2, 1000, 1000)]       // never taller than the work area
    public void OuterSizeRoundsUpAddsTheFrameAndStopsAtTheWorkArea(
        double measured, double scale, int workHeight, int expectedHeight)
    {
        var (width, height) = ContentSizedPopupLayout.OuterSize(320, measured, scale, 14, 10, workHeight);

        Assert.Equal((int)Math.Ceiling(320 * scale) + 14, width);
        Assert.Equal(expectedHeight, height);
    }

    [Fact]
    public void OriginCentresAndKeepsAnOverTallWindowsTopOnScreen()
    {
        Assert.Equal((150, 120), ContentSizedPopupLayout.Origin(100, 100, 600, 440, 500, 400));
        Assert.Equal((150, 100), ContentSizedPopupLayout.Origin(100, 100, 600, 440, 500, 500));
    }
}
