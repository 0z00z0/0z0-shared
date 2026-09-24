using Xunit;

namespace ZeroZero.Win32.Tests;

/// <summary>Placement arithmetic alone: no window, no monitor, no scheduler. Every work area below
/// is wider than it is tall and no rectangle's left edge equals its top, so an axis mixed up with
/// the other shows.</summary>
public class WindowFitTests
{
    private static readonly NativeRect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void ARectangleThatAlreadyFitsIsLeftWhereItIs()
    {
        var desired = new NativeRect(100, 40, 900, 640);

        Assert.Equal(desired, WindowFit.Fit(desired, requiredHeight: 0, WorkArea));
    }

    [Fact]
    public void ARectangleShorterThanItsContentGrowsToTheHeightTheContentNeeds()
    {
        var desired = new NativeRect(100, 40, 900, 340);

        NativeRect fitted = WindowFit.Fit(desired, requiredHeight: 500, WorkArea);

        Assert.Equal(500, fitted.Height);
        Assert.Equal(800, fitted.Width);
        Assert.Equal(100, fitted.Left);
    }

    [Fact]
    public void ARectangleTallerThanItsContentIsNotShrunkToIt()
    {
        var desired = new NativeRect(100, 40, 900, 740);

        Assert.Equal(700, WindowFit.Fit(desired, requiredHeight: 200, WorkArea).Height);
    }

    [Fact]
    public void ARectangleLargerThanTheWorkAreaIsCutDownToIt()
    {
        var desired = new NativeRect(0, 0, 2400, 1300);

        NativeRect fitted = WindowFit.Fit(desired, requiredHeight: 0, WorkArea);

        Assert.Equal(WorkArea.Width, fitted.Width);
        Assert.Equal(WorkArea.Height, fitted.Height);
    }

    [Fact]
    public void ARectangleHangingOffAnEdgeIsMovedBackInside()
    {
        var desired = new NativeRect(1700, 900, 2100, 1100);

        NativeRect fitted = WindowFit.Fit(desired, requiredHeight: 0, WorkArea);

        Assert.Equal(new NativeRect(1520, 840, 1920, 1040), fitted);
    }

    [Fact]
    public void ARectangleOnNoPartOfTheWorkAreaIsRecentredRatherThanJammedIntoACorner()
    {
        // The monitor it was saved on has gone. Clamping alone would put it against an edge.
        var desired = new NativeRect(3000, 1500, 3400, 1700);

        NativeRect fitted = WindowFit.Fit(desired, requiredHeight: 0, WorkArea);

        Assert.Equal(new NativeRect(760, 420, 1160, 620), fitted);
    }

    [Fact]
    public void ARectangleMerelyTouchingTheWorkAreasEdgeCountsAsOffScreen()
    {
        // Half-open bounds: a right edge equal to the work area's left shares no pixel with it.
        var touching = new NativeRect(-400, 300, 0, 500);

        NativeRect fitted = WindowFit.Fit(touching, requiredHeight: 0, WorkArea);

        Assert.Equal(new NativeRect(760, 420, 1160, 620), fitted);
    }

    [Fact]
    public void AHeightForContentCarriesTheChromeTheWindowAlreadyHas()
    {
        // 600 tall showing 540 of content: 60 of chrome, which 800 of content keeps.
        Assert.Equal(860, WindowFit.HeightForContent(600, 800, 540, minimumHeight: 200));
    }

    [Fact]
    public void AHeightForContentShrinksToTheContentButNotBelowTheFloor()
    {
        Assert.Equal(260, WindowFit.HeightForContent(600, 200, 540, minimumHeight: 200));
        Assert.Equal(200, WindowFit.HeightForContent(600, 100, 540, minimumHeight: 200));
    }

    [Fact]
    public void TheCapIsTheGivenShareOfTheWorkArea()
    {
        Assert.Equal(832, WindowFit.HeightCap(1040, WindowFit.DefaultHeightFraction));
        Assert.Equal(520, WindowFit.HeightCap(1040, 0.5));
    }

    [Fact]
    public void ContentThatFitsUnderTheCapGetsTheHeightItAsksForPlusTheChrome()
    {
        // 300 units at 150 % is 450 physical pixels, and the frame adds 40.
        Assert.Equal(490, WindowFit.ContentFittedHeight(300, 1.5, chromeHeight: 40, workAreaHeight: 1040,
                                                        minimumContentUnits: 100, WindowFit.DefaultHeightFraction));
    }

    [Fact]
    public void ContentTallerThanTheCapIsCappedSoTheScrollerTakesOver()
    {
        Assert.Equal(832, WindowFit.ContentFittedHeight(2000, 1.5, chromeHeight: 40, workAreaHeight: 1040,
                                                        minimumContentUnits: 100, WindowFit.DefaultHeightFraction));
    }

    [Fact]
    public void ContentShorterThanTheFloorIsRaisedToIt()
    {
        // 50 units of content, a floor of 200: 200 at 150 % is 300, and the frame adds 40.
        Assert.Equal(340, WindowFit.ContentFittedHeight(50, 1.5, chromeHeight: 40, workAreaHeight: 1040,
                                                        minimumContentUnits: 200, WindowFit.DefaultHeightFraction));
    }

    [Fact]
    public void AFloorTallerThanTheCapWinsOverTheCap()
    {
        // 600 units at 150 % is 900 physical pixels and the frame adds 40, so the floor is 940 —
        // past the 832 cap. A window shorter than its floor has no room for what it must always
        // show, while one past the fraction is only taller than the policy prefers.
        Assert.Equal(940, WindowFit.ContentFittedHeight(100, 1.5, chromeHeight: 40, workAreaHeight: 1040,
                                                        minimumContentUnits: 600, WindowFit.DefaultHeightFraction));
    }

    [Fact]
    public void NothingExceedsTheWorkAreaItself_NotEvenTheFloor()
    {
        // A floor of 900 units at 150 % is 1390 with the frame, taller than the whole work area.
        Assert.Equal(1040, WindowFit.ContentFittedHeight(2000, 1.5, chromeHeight: 40, workAreaHeight: 1040,
                                                         minimumContentUnits: 900, WindowFit.DefaultHeightFraction));
    }

    [Fact]
    public void ANegativeChromeReadingIsIgnoredRatherThanSubtracted()
    {
        // A client area reported larger than the frame would otherwise shrink the window.
        Assert.Equal(450, WindowFit.ContentFittedHeight(300, 1.5, chromeHeight: -80, workAreaHeight: 1040,
                                                        minimumContentUnits: 100, WindowFit.DefaultHeightFraction));
    }

    [Theory]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, 1.75, 175)]
    [InlineData(101, 1.5, 152)]   // rounded up: a pixel short clips the last row
    [InlineData(100, 0, 100)]     // an unknown scale is 100 %, never a zero-sized window
    [InlineData(100, -2, 100)]
    public void UnitsConvertToPhysicalPixelsAtTheGivenScale(int units, double scale, int expected)
    {
        Assert.Equal(expected, WindowFit.ToPhysicalPixels(units, scale));
    }
}
