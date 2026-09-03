using Xunit;
using ZeroZero.SettingsShell.WinUI;
using ZeroZero.Win32;

namespace ZeroZero.SettingsShell.Tests;

/// <summary>
/// Where the window opens and what it remembers, as arithmetic: a saved rectangle kept, shrunk
/// or moved back onto a monitor; nothing saved centred on the cursor's monitor at its scale; a
/// maximised or minimised rectangle refused.
/// </summary>
public class WindowPlacementTests
{
    /// <summary>A 1920×1080 work area starting at (100, 50), the way a secondary monitor to the
    /// right of a primary reports one.</summary>
    private static readonly NativeRect Cursor = new(100, 50, 2020, 1130);

    private static NativeRect NoMonitorLookup(NativeRect _) =>
        throw new InvalidOperationException("Nothing saved, so no monitor should be looked up.");

    [Fact]
    public void NothingSaved_OpensAtTheDefaultClientSizeScaledPlusTheFrame_CentredOnTheCursorMonitor()
    {
        var rect = WindowPlacement.Opening(
            saved: null, Cursor, cursorScale: 1.5,
            defaultClientWidth: 960, defaultClientHeight: 640, frame: (16, 39), NoMonitorLookup);

        // 960 × 1.5 + 16 = 1456 wide, 640 × 1.5 + 39 = 999 tall.
        Assert.Equal(1456, rect.Width);
        Assert.Equal(999, rect.Height);
        Assert.Equal(100 + (1920 - 1456) / 2, rect.Left);
        Assert.Equal(50 + (1080 - 999) / 2, rect.Top);
    }

    [Fact]
    public void NothingSaved_OnAMonitorSmallerThanTheDefault_FillsItsWorkArea()
    {
        var small = new NativeRect(0, 0, 800, 600);

        var rect = WindowPlacement.Opening(null, small, 1.0, 960, 640, (0, 0), NoMonitorLookup);

        Assert.Equal(small, rect);
    }

    [Theory]
    [InlineData(0, 400)]
    [InlineData(400, 0)]
    [InlineData(-10, 400)]
    public void ASavedRectangleWithNoArea_CountsAsNothingSaved(int width, int height)
    {
        var rect = WindowPlacement.Opening(
            new WindowRect(10, 10, width, height), Cursor, 1.0, 960, 640, (0, 0), NoMonitorLookup);

        Assert.Equal(960, rect.Width);
        Assert.Equal(640, rect.Height);
    }

    [Fact]
    public void ASavedRectangleInsideItsMonitor_OpensExactlyThere()
    {
        var saved = new WindowRect(300, 200, 1000, 700);
        NativeRect? askedFor = null;

        var rect = WindowPlacement.Opening(saved, Cursor, 2.0, 960, 640, (16, 39), wanted =>
        {
            askedFor = wanted;
            return Cursor;
        });

        Assert.Equal(new NativeRect(300, 200, 1300, 900), rect);
        // The monitor is looked up for the saved rectangle, not for the cursor.
        Assert.Equal(new NativeRect(300, 200, 1300, 900), askedFor);
    }

    [Fact]
    public void ASavedRectangleThatHasStrayedOffItsMonitor_IsMovedBackInside()
    {
        // Past the right and bottom edges of the work area by 200 and 100.
        var saved = new WindowRect(1220, 530, 1000, 700);

        var rect = WindowPlacement.Opening(saved, Cursor, 1.0, 960, 640, (0, 0), _ => Cursor);

        Assert.Equal(1000, rect.Width);
        Assert.Equal(700, rect.Height);
        Assert.Equal(Cursor.Right, rect.Right);
        Assert.Equal(Cursor.Bottom, rect.Bottom);
    }

    [Fact]
    public void ASavedRectangleOnAMonitorThatHasGone_LandsOnTheNearestOne()
    {
        // Saved on a monitor to the left that is no longer there; the nearest is the primary.
        var primary = new NativeRect(0, 0, 1920, 1040);
        var saved = new WindowRect(-1800, 100, 900, 600);

        var rect = WindowPlacement.Opening(saved, Cursor, 1.0, 960, 640, (0, 0), _ => primary);

        Assert.Equal(new NativeRect(0, 100, 900, 700), rect);
    }

    [Fact]
    public void ASavedRectangleLargerThanItsMonitor_IsShrunkToTheWorkAreaAndPinnedToItsOrigin()
    {
        var saved = new WindowRect(400, 300, 2600, 1500);

        var rect = WindowPlacement.Opening(saved, Cursor, 1.0, 960, 640, (0, 0), _ => Cursor);

        Assert.Equal(Cursor, rect);
    }

    [Fact]
    public void Remember_KeepsARestoredRectangle()
    {
        var rect = new WindowRect(10, 20, 800, 600);

        Assert.Equal(rect, WindowPlacement.Remember(restored: true, rect));
    }

    [Fact]
    public void Remember_RefusesAMaximisedOrMinimisedRectangle()
    {
        // Restored from it, a maximised geometry fills the screen with no way back and a minimised
        // one opens off it.
        Assert.Null(WindowPlacement.Remember(restored: false, new WindowRect(0, 0, 1920, 1040)));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, 0)]
    public void Remember_RefusesARectangleWithNoArea(int width, int height)
    {
        Assert.Null(WindowPlacement.Remember(restored: true, new WindowRect(10, 20, width, height)));
    }
}
