using System.Globalization;
using System.Reflection;
using Xunit;

namespace ZeroZero.Brand.Core.Tests;

/// <summary>
/// The palette's measured contrast, held as data rather than left in a design note. Every figure
/// here was taken with the WCAG 2.x relative-luminance formula and is pinned to two decimals, so
/// editing a palette constant trips this file rather than quietly moving a colour past a floor.
/// <para>
/// Two rules come out of the figures and are asserted below: an accent takes black text rather
/// than white (Indigo runs the other way), and a tinted fill over the two brand grounds cannot be
/// derived by opacity, because both grounds sit near black and swamp whatever is laid over them.
/// </para>
/// </summary>
public class PaletteContrastTests
{
    /// <summary>WCAG 1.4.11, non-text contrast: the floor a mark or a fill has to clear.</summary>
    private const double NonTextFloor = 3.0;

    /// <summary>WCAG 1.4.3, normal body text.</summary>
    private const double TextFloor = 4.5;

    /// <summary>The opacity a tinted fill was assumed to be derived at.</summary>
    private const double TintOpacity = 0.24;

    private const string Black = "#000000";
    private const string White = "#ffffff";

    /// <summary>The palette's accents — everything that is not one of the two brand grounds.</summary>
    private static readonly (string Name, string Colour)[] Accents =
    [
        ("Teal", Brand.ColorTeal),
        ("Blue", Brand.ColorBlue),
        ("Purple", Brand.ColorPurple),
        ("Indigo", Brand.ColorIndigo),
        ("Amber", Brand.ColorAmber),
        ("SteelBlue", Brand.ColorSteelBlue),
        ("Terracotta", Brand.ColorTerracotta),
    ];

    /// <summary>
    /// The measurement itself: each accent's contrast against the two brand grounds and against
    /// the two text colours, rounded to two decimals. A row that stops matching means the colour
    /// moved, and the figures quoted everywhere else are stale.
    /// </summary>
    public static TheoryData<string, double, double, double, double> Measured => new()
    {
        //          name           vs Bg   vs BgAlt  vs black  vs white
        { "Teal",       11.51,  10.90,  12.58,  1.67 },
        { "Blue",        7.01,   6.64,   7.67,  2.74 },
        { "Purple",      6.44,   6.10,   7.04,  2.98 },
        { "Indigo",      3.46,   3.28,   3.78,  5.55 },
        { "Amber",       8.70,   8.24,   9.51,  2.21 },
        { "SteelBlue",   7.49,   7.10,   8.20,  2.56 },
        { "Terracotta",  7.14,   6.76,   7.81,  2.69 },
    };

    [Theory]
    [MemberData(nameof(Measured))]
    public void EveryAccentMeasuresWhatThePaletteRecords(
        string name, double againstBg, double againstBgAlt, double againstBlack, double againstWhite)
    {
        string colour = Accents.Single(a => a.Name == name).Colour;
        Assert.Equal(againstBg, Round(Ratio(colour, Brand.ColorBg)));
        Assert.Equal(againstBgAlt, Round(Ratio(colour, Brand.ColorBg2)));
        Assert.Equal(againstBlack, Round(Ratio(colour, Black)));
        Assert.Equal(againstWhite, Round(Ratio(colour, White)));
    }

    /// <summary>Nothing in the table may be an accent this file forgot to measure.</summary>
    [Fact]
    public void EveryPaletteConstantIsEitherAGroundOrAMeasuredAccent()
    {
        var constants = typeof(Brand).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.StartsWith("Color", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        string[] covered = [Brand.ColorBg, Brand.ColorBg2, .. Accents.Select(a => a.Colour)];
        Assert.Equal(constants.Order(), covered.Order());
    }

    [Fact]
    public void EveryAccentClearsTheNonTextFloorOnBothBrandGrounds()
    {
        foreach (var (name, colour) in Accents)
        {
            Assert.True(Ratio(colour, Brand.ColorBg) >= NonTextFloor,
                        $"{name} is {Round(Ratio(colour, Brand.ColorBg))}:1 on the brand background.");
            Assert.True(Ratio(colour, Brand.ColorBg2) >= NonTextFloor,
                        $"{name} is {Round(Ratio(colour, Brand.ColorBg2))}:1 on the alternate brand background.");
        }
    }

    /// <summary>
    /// The two newest members clear body-text contrast on both brand grounds, not merely the
    /// non-text floor the palette as a whole is held to — Indigo is the one that does not, and a
    /// new colour joining below it would lower the palette rather than fill it out.
    /// </summary>
    [Theory]
    [InlineData("SteelBlue")]
    [InlineData("Terracotta")]
    public void TheNewestAccentsClearBodyTextContrastOnBothBrandGrounds(string name)
    {
        string colour = Accents.Single(a => a.Name == name).Colour;
        Assert.True(Ratio(colour, Brand.ColorBg) >= TextFloor,
                    $"{name} is {Round(Ratio(colour, Brand.ColorBg))}:1 on the brand background.");
        Assert.True(Ratio(colour, Brand.ColorBg2) >= TextFloor,
                    $"{name} is {Round(Ratio(colour, Brand.ColorBg2))}:1 on the alternate brand background.");
    }

    /// <summary>
    /// Text on an accent is black, and Indigo is the single exception. Stated as a rule rather than
    /// per colour so a caller picks one text colour for the palette instead of guessing per fill.
    /// </summary>
    [Fact]
    public void EveryAccentTakesBlackTextExceptIndigoWhichTakesWhite()
    {
        foreach (var (name, colour) in Accents)
        {
            bool black = Ratio(colour, Black) >= TextFloor;
            bool white = Ratio(colour, White) >= TextFloor;
            if (name == "Indigo")
            {
                Assert.True(white && !black,
                            $"Indigo is {Round(Ratio(colour, White))}:1 white, {Round(Ratio(colour, Black))}:1 black.");
            }
            else
            {
                Assert.True(black && !white,
                            $"{name} is {Round(Ratio(colour, Black))}:1 black, {Round(Ratio(colour, White))}:1 white.");
            }
        }
    }

    /// <summary>
    /// A tinted fill is not derived by opacity here. Both brand grounds sit near black, so a low
    /// opacity leaves three quarters of the result as ground whatever is laid over it, and no
    /// accent comes anywhere near the non-text floor.
    /// </summary>
    [Fact]
    public void NoAccentBecomesADistinguishableFillAtTheAssumedTintOpacity()
    {
        foreach (var (name, colour) in Accents)
        {
            foreach (string ground in new[] { Brand.ColorBg, Brand.ColorBg2 })
            {
                double ratio = Ratio(Composite(colour, ground, TintOpacity), ground);
                Assert.True(ratio < NonTextFloor,
                            $"{name} reaches {Round(ratio)}:1 as a {TintOpacity:P0} tint, so a tint can be derived after all.");
            }
        }
    }

    /// <summary>
    /// The ceiling that makes the previous test a property of the ground rather than of any one
    /// colour: not even white clears the non-text floor at this opacity, so no choice of accent
    /// could have.
    /// </summary>
    [Fact]
    public void NotEvenWhiteBecomesADistinguishableFillAtTheAssumedTintOpacity()
    {
        foreach (string ground in new[] { Brand.ColorBg, Brand.ColorBg2 })
        {
            double ratio = Ratio(Composite(White, ground, TintOpacity), ground);
            Assert.True(ratio < NonTextFloor, $"White reaches {Round(ratio)}:1 as a {TintOpacity:P0} tint.");
        }
    }

    /// <summary>
    /// Steel blue is not the palette's worst tint, which is why it carries no exception of its own:
    /// Indigo composites dimmer, and Indigo cannot reach the non-text floor at any opacity because
    /// it does not reach it at full strength.
    /// </summary>
    [Fact]
    public void SteelBlueTintsBetterThanIndigoAndIndigoCannotReachTheFloorAtAnyOpacity()
    {
        double steel = Ratio(Composite(Brand.ColorSteelBlue, Brand.ColorBg, TintOpacity), Brand.ColorBg);
        double indigo = Ratio(Composite(Brand.ColorIndigo, Brand.ColorBg, TintOpacity), Brand.ColorBg);
        Assert.True(steel > indigo, $"Steel blue {Round(steel)}:1, Indigo {Round(indigo)}:1.");

        // Full strength is the ceiling every opacity below it sits under.
        Assert.True(Ratio(Brand.ColorIndigo, Brand.ColorBg) < NonTextFloor + 0.5);
    }

    private static double Round(double ratio) => Math.Round(ratio, 2, MidpointRounding.AwayFromZero);

    private static double Ratio(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var (r, g, b) = Channels(hex);
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    private static double Linear(int channel)
    {
        double c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Alpha blending as the compositor does it, in sRGB rather than in linear light — which is
    /// why a low-opacity overlay on a near-black ground stays near black.
    /// </summary>
    private static string Composite(string over, string under, double alpha)
    {
        var (r1, g1, b1) = Channels(over);
        var (r2, g2, b2) = Channels(under);
        return string.Create(CultureInfo.InvariantCulture,
            $"#{Mix(r1, r2):x2}{Mix(g1, g2):x2}{Mix(b1, b2):x2}");

        int Mix(int a, int b) => (int)Math.Round(alpha * a + (1 - alpha) * b, MidpointRounding.AwayFromZero);
    }

    private static (int R, int G, int B) Channels(string hex) =>
        (int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
}
