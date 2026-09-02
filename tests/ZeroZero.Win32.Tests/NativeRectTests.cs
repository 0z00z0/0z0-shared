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

    // Every rectangle below is wider than it is tall: a square hides an axis mixed up with the other.

    [Fact]
    public void ClampInto_MovesARectanglePastTheRightAndBottomEdgesBackInside()
    {
        var rect = new NativeRect(900, 700, 1200, 850);

        var clamped = rect.ClampInto(Bounds);

        Assert.Equal(new NativeRect(700, 650, 1000, 800), clamped);
    }

    [Fact]
    public void ClampInto_MovesARectanglePastTheLeftAndTopEdgesBackInside()
    {
        var rect = new NativeRect(-50, -20, 250, 80);

        var clamped = rect.ClampInto(Bounds);

        Assert.Equal(new NativeRect(0, 0, 300, 100), clamped);
    }

    [Fact]
    public void ClampInto_KeepsTheSize()
    {
        var rect = new NativeRect(950, 790, 1250, 890);

        var clamped = rect.ClampInto(Bounds);

        Assert.Equal(rect.Width, clamped.Width);
        Assert.Equal(rect.Height, clamped.Height);
    }

    [Fact]
    public void ClampInto_PinsARectangleWiderThanTheBoundsToTheirLeftEdgeAndKeepsItsTop()
    {
        var rect = new NativeRect(300, 300, 1500, 700);

        var clamped = rect.ClampInto(Bounds);

        Assert.Equal(new NativeRect(0, 300, 1200, 700), clamped);
    }

    [Fact]
    public void ClampInto_PinsARectangleLargerThanTheBoundsInBothDirectionsToTheirOrigin()
    {
        var rect = new NativeRect(300, 300, 1500, 1300);

        var clamped = rect.ClampInto(Bounds);

        Assert.Equal(new NativeRect(0, 0, 1200, 1000), clamped);
    }

    [Fact]
    public void ClampInto_HonoursBoundsThatDoNotStartAtTheOrigin()
    {
        var secondMonitor = new NativeRect(1920, 0, 3840, 1080);
        var rect = new NativeRect(3800, 1000, 4100, 1150);

        var clamped = rect.ClampInto(secondMonitor);

        Assert.Equal(new NativeRect(3540, 930, 3840, 1080), clamped);
    }
}
