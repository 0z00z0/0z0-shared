using Xunit;

namespace ZeroZero.Win32.Tests;

public class NativeRectTests
{
    private static readonly NativeRect Bounds = new(0, 0, 1000, 800);

    [Fact]
    public void Width_And_Height_AreTheRightAndBottomEdgesLessTheLeftAndTop()
    {
        var rect = new NativeRect(10, 20, 110, 220);

        Assert.Equal(100, rect.Width);
        Assert.Equal(200, rect.Height);
    }

    [Fact]
    public void ClampInto_LeavesARectangleAlreadyInsideWhereItIs()
    {
        var rect = new NativeRect(100, 100, 300, 250);

        Assert.Equal(rect, rect.ClampInto(Bounds));
    }

    [Fact]
    public void ClampInto_MovesARectanglePastTheRightAndBottomEdgesBackInside()
    {
        var rect = new NativeRect(900, 700, 1100, 900);

        var clamped = rect.ClampInto(Bounds);

        Assert.Equal(new NativeRect(800, 600, 1000, 800), clamped);
    }

    [Fact]
    public void ClampInto_MovesARectanglePastTheLeftAndTopEdgesBackInside()
    {
        var rect = new NativeRect(-50, -20, 150, 180);

        var clamped = rect.ClampInto(Bounds);

        Assert.Equal(new NativeRect(0, 0, 200, 200), clamped);
    }

    [Fact]
    public void ClampInto_KeepsTheSize()
    {
        var rect = new NativeRect(950, 790, 1150, 990);

        var clamped = rect.ClampInto(Bounds);

        Assert.Equal(rect.Width, clamped.Width);
        Assert.Equal(rect.Height, clamped.Height);
    }

    [Fact]
    public void ClampInto_PinsARectangleLargerThanTheBoundsToTheirOrigin()
    {
        var rect = new NativeRect(300, 300, 1500, 1300);

        var clamped = rect.ClampInto(Bounds);

        Assert.Equal(new NativeRect(0, 0, 1200, 1000), clamped);
    }

    [Fact]
    public void ClampInto_HonoursBoundsThatDoNotStartAtTheOrigin()
    {
        var secondMonitor = new NativeRect(1920, 0, 3840, 1080);
        var rect = new NativeRect(3800, 1000, 4000, 1200);

        var clamped = rect.ClampInto(secondMonitor);

        Assert.Equal(new NativeRect(3640, 880, 3840, 1080), clamped);
    }
}
