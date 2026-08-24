using Xunit;

namespace ZeroZero.Brand.Core.Tests;

/// <summary>The CLI About banner. Every assertion drives the real
/// <see cref="ConsoleBanner.Print(AboutInfo)"/> and reads what it actually wrote.</summary>
public class ConsoleBannerTests
{
    private const string Rule = "==========================================";

    private static AboutInfo Sample(params ExternalLibrary[] libraries) => new()
    {
        AppName           = "ChargeKeeper",
        Version           = "1.4.2",
        Description       = "Keeps a laptop battery inside its healthy charge band.",
        RepoUrl           = "https://github.com/0z00z0/chargekeeper",
        ExternalLibraries = libraries,
    };

    /// <summary>Captures what Print wrote. Console.Out is process-wide state, so every test that
    /// redirects it lives in this one class — xUnit runs a class's tests sequentially, which keeps
    /// one capture from swallowing another's output.</summary>
    private static string Capture(AboutInfo info)
    {
        var original = Console.Out;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);
            ConsoleBanner.Print(info);
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static string[] Lines(string banner) =>
        banner.Split(Environment.NewLine).Select(l => l.TrimEnd()).ToArray();

    [Fact]
    public void Print_WritesSomething()
        => Assert.NotEmpty(Capture(Sample()));

    [Fact]
    public void Print_OpensAndClosesWithARule()
    {
        var lines = Lines(Capture(Sample())).Where(l => l.Length > 0).ToArray();

        Assert.Equal(Rule, lines[0]);
        Assert.Equal(Rule, lines[^1]);
    }

    [Fact]
    public void Print_UsesThreeRules_TwoAroundTheBrandBlockAndOneAtTheEnd()
        => Assert.Equal(3, Lines(Capture(Sample())).Count(l => l == Rule));

    [Fact]
    public void Print_LeadsWithTheStudioNameAndTagline()
    {
        var lines = Lines(Capture(Sample()));

        Assert.Equal($" {Brand.StudioName}", lines[1]);
        Assert.Equal($" {Brand.Tagline}", lines[2]);
    }

    [Fact]
    public void Print_RendersTheAppNameAndVersionOnOneLine()
        => Assert.Contains(" ChargeKeeper v1.4.2", Lines(Capture(Sample())));

    [Fact]
    public void Print_RendersTheDescription()
        => Assert.Contains(" Keeps a laptop battery inside its healthy charge band.", Lines(Capture(Sample())));

    [Fact]
    public void Print_ListsTheRepoAndBothStudioLinks()
    {
        var lines = Lines(Capture(Sample()));

        Assert.Contains(" https://github.com/0z00z0/chargekeeper", lines);
        Assert.Contains($" {Brand.WebsiteUrl}", lines);
        Assert.Contains($" {Brand.BuyMeACoffeeUrl}", lines);
    }

    [Fact]
    public void Print_WithNoLibraries_OmitsTheCreditsSection()
        => Assert.DoesNotContain("External libraries", Capture(Sample()));

    [Fact]
    public void Print_WithLibraries_HeadsTheCreditsSection()
        => Assert.Contains(
            " External libraries:",
            Lines(Capture(Sample(new ExternalLibrary("NLog", "NLog contributors", "File logging", "BSD-3-Clause")))));

    [Fact]
    public void Print_CreditsEveryLibraryWithItsAuthorPurposeAndLicence()
    {
        var banner = Capture(Sample(
            new ExternalLibrary("NLog", "NLog contributors", "File logging", "BSD-3-Clause"),
            new ExternalLibrary("xunit", "xunit contributors", "Unit testing", "Apache-2.0", "https://xunit.net")));

        Assert.Contains("   NLog (NLog contributors) - File logging [BSD-3-Clause]", Lines(banner));
        Assert.Contains("   xunit (xunit contributors) - Unit testing [Apache-2.0]", Lines(banner));
    }

    /// <summary>The banner's whole reason for existing: it has to survive a legacy code page and
    /// redirected output, so no brand glyph and no box drawing may reach it.</summary>
    [Fact]
    public void Print_EmitsAsciiOnly_WhenTheSuppliedDataIsAscii()
    {
        var banner = Capture(Sample(new ExternalLibrary("NLog", "NLog contributors", "File logging", "BSD-3-Clause")));

        Assert.All(banner, c => Assert.InRange(c, (char)0x09, (char)0x7e));
    }

    [Fact]
    public void Print_UsesNoBrandGlyphAndNoBoxDrawing()
    {
        var banner = Capture(Sample());

        Assert.DoesNotContain("Ø", banner);   // Ø, the site mark
        Assert.DoesNotContain("─", banner);   // ─, box drawing
        Assert.DoesNotContain("©", banner);   // ©, not part of the console banner
    }

    /// <summary>Print reads Console.Out at call time, so a caller that redirected it gets the
    /// banner — which is what makes every assertion above possible, and what lets a CLI tool pipe
    /// its own banner into a log.</summary>
    [Fact]
    public void Print_WritesToTheCurrentConsoleOut_NotACachedOne()
    {
        var original = Console.Out;
        try
        {
            using var first  = new StringWriter();
            using var second = new StringWriter();

            Console.SetOut(first);
            ConsoleBanner.Print(Sample());
            Console.SetOut(second);
            ConsoleBanner.Print(Sample() with { AppName = "HyperVManagerTray" });

            Assert.Contains("ChargeKeeper", first.ToString());
            Assert.DoesNotContain("HyperVManagerTray", first.ToString());
            Assert.Contains("HyperVManagerTray", second.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
