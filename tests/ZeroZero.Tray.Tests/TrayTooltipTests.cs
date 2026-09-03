using Xunit;
using ZeroZero.Tray.WinUI;

namespace ZeroZero.Tray.Tests;

public class TrayTooltipTests
{
    [Fact]
    public void TheLimitIsTheShellsTipLessItsTerminator()
    {
        Assert.Equal(127, TrayTooltip.MaxUnits);
    }

    [Fact]
    public void Compose_DropsBlankLinesAndRepeats()
    {
        Assert.Equal("A\nB", TrayTooltip.Compose("A", "", null, "  ", "A", "B"));
    }

    [Fact]
    public void Compose_TrimsEachLine()
    {
        Assert.Equal("A\nB", TrayTooltip.Compose("  A  ", "\tB "));
    }

    [Fact]
    public void Compose_TakesALineThatFitsWhole()
    {
        string line = new('a', TrayTooltip.MaxUnits);
        Assert.Equal(line, TrayTooltip.Compose(line));
    }

    [Fact]
    public void Compose_CutsALongLineToTheLimitWithAnEllipsis()
    {
        string text = TrayTooltip.Compose(new string('a', 200));

        Assert.Equal(TrayTooltip.MaxUnits, text.Length);
        Assert.EndsWith("a…", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_KeepsTheSuffixWholeWhenTheBodyIsCut()
    {
        string text = TrayTooltip.Compose([new TrayTooltipLine(new string('n', 200), " · 84 %")]);

        Assert.Equal(TrayTooltip.MaxUnits, text.Length);
        Assert.EndsWith("n… · 84 %", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_NeverCutsBetweenTheHalvesOfASurrogatePair()
    {
        // 125 units, then a two-unit character straddling the cut, then one more unit.
        string body = new string('a', 125) + "\U0001F600" + "b";

        string text = TrayTooltip.Compose(body);

        Assert.True(text.Length <= TrayTooltip.MaxUnits);
        Assert.DoesNotContain(text, char.IsSurrogate);
        Assert.Equal(new string('a', 125) + "…", text);
    }

    [Fact]
    public void Compose_CutsALaterLineToWhatRemains()
    {
        string first = new('a', 100);
        string text = TrayTooltip.Compose(first, new string('b', 100));

        Assert.Equal(TrayTooltip.MaxUnits, text.Length);
        Assert.Equal(first + "\n" + new string('b', 25) + "…", text);
    }

    [Fact]
    public void Compose_DropsALineNothingOfWhichWouldFitAndStopsThere()
    {
        string first = new('a', 126);

        // One unit remains after the separator: room for the ellipsis alone, which says nothing.
        Assert.Equal(first, TrayTooltip.Compose(first, "x", "y"));
    }

    [Fact]
    public void Compose_DropsALineWhoseSuffixCannotBeProtectedAndTakesNothingAfterIt()
    {
        string first = new('a', 120);

        // Six units remain: the suffix alone is seven, so the line is dropped, and the short line
        // after it is not taken in its place, which would put a later line above an earlier one.
        string text = TrayTooltip.Compose([new TrayTooltipLine(first), new TrayTooltipLine("name", " · 84 %"), new TrayTooltipLine("x")]);

        Assert.Equal(first, text);
    }

    [Fact]
    public void Compose_DropsASuffixOnlyLineWithNoBody()
    {
        Assert.Equal("", TrayTooltip.Compose([new TrayTooltipLine("", "  "), new TrayTooltipLine(null)]));
    }
}
