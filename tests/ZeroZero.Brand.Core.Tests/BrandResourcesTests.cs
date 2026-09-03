using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace ZeroZero.Brand.Core.Tests;

/// <summary>
/// The brand resource dictionary declares the palette a second time, in the form XAML consumes, and
/// nothing in a build ties the two declarations together. The dictionary lives in a Windows-only
/// project this one cannot reference, so it is read as data and held against the constants here.
/// </summary>
public class BrandResourcesTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Which constant each colour key repeats. A constant with no row here, or a row
    /// naming no constant, fails the first test rather than drifting unnoticed.</summary>
    private static readonly (string Constant, string Key)[] Palette =
    [
        (nameof(Brand.ColorBg), "BrandBackground"),
        (nameof(Brand.ColorBg2), "BrandBackgroundAlt"),
        (nameof(Brand.ColorTeal), "BrandTeal"),
        (nameof(Brand.ColorBlue), "BrandBlue"),
        (nameof(Brand.ColorPurple), "BrandPurple"),
        (nameof(Brand.ColorIndigo), "BrandIndigo"),
        (nameof(Brand.ColorAmber), "BrandAmber"),
        (nameof(Brand.ColorSteelBlue), "BrandSteelBlue"),
        (nameof(Brand.ColorTerracotta), "BrandTerracotta"),
    ];

    private static readonly string[] PaletteThemes = ["Default", "Light"];

    [Fact]
    public void EveryPaletteConstantIsDeclaredOnceAndRepeatedInLightAndDark()
    {
        var constants = typeof(Brand).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.StartsWith("Color", StringComparison.Ordinal))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Assert.Equal(constants.Keys.Order(), Palette.Select(p => p.Constant).Order());

        var themes = Themes();
        foreach (string theme in PaletteThemes)
        {
            foreach (var (constant, key) in Palette)
            {
                string? declared = Colour(themes[theme], key + "Colour");
                Assert.True(declared is not null, $"{theme} declares no {key}Colour.");
                Assert.True(string.Equals(declared, constants[constant], StringComparison.OrdinalIgnoreCase),
                            $"{theme} {key}Colour is {declared}; Brand.{constant} is {constants[constant]}.");
            }
        }
    }

    [Fact]
    public void EveryThemeCarriesTheSameKeys()
    {
        var themes = Themes();
        Assert.Equal(["Default", "HighContrast", "Light"], themes.Keys.Order());

        var expected = Palette.SelectMany(p => new[] { p.Key + "Colour", p.Key + "Brush" }).Order().ToArray();
        foreach (var (theme, dictionary) in themes)
        {
            var keys = dictionary.Elements().Select(Key).Order().ToArray();
            Assert.True(expected.SequenceEqual(keys),
                        $"{theme} holds [{string.Join(", ", keys)}]; every theme holds [{string.Join(", ", expected)}].");
        }
    }

    [Fact]
    public void EveryBrushInLightAndDarkTakesItsOwnColourKey()
    {
        var themes = Themes();
        foreach (string theme in PaletteThemes)
        {
            foreach (var (_, key) in Palette)
            {
                string? colour = Brush(themes[theme], key + "Brush");
                Assert.Equal($"{{StaticResource {key}Colour}}", colour);
            }
        }
    }

    [Fact]
    public void HighContrastCarriesNoPaletteValue()
    {
        // The mode exists so the user's colours outrank the studio's: a literal here would put the
        // palette on a high-contrast screen, and a reference to anything but a system colour would
        // route back to it.
        var highContrast = Themes()["HighContrast"];
        foreach (var entry in highContrast.Elements())
        {
            string value = entry.Name == Presentation + "Color" ? entry.Value.Trim() : entry.Attribute("Color")!.Value;
            Assert.True(value.StartsWith("{ThemeResource SystemColor", StringComparison.Ordinal),
                        $"HighContrast {Key(entry)} is {value}.");
        }
    }

    [Fact]
    public void TheTypefaceKeyNamesTheFaceTheAboutControlUses()
    {
        string dictionaryFace = Dictionary().Root!
            .Elements(Presentation + "FontFamily")
            .Single(e => Key(e) == "BrandFontFamily").Value.Trim();

        var about = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "BrandAboutControl.xaml"));
        string controlFace = about.Descendants(Presentation + "FontFamily")
            .Single(e => Key(e) == "BrandFont").Value.Trim();

        Assert.Equal(controlFace, dictionaryFace);
    }

    private static XDocument Dictionary() =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "BrandResources.xaml"));

    private static Dictionary<string, XElement> Themes() =>
        Dictionary().Root!
            .Element(Presentation + "ResourceDictionary.ThemeDictionaries")!
            .Elements(Presentation + "ResourceDictionary")
            .ToDictionary(Key);

    private static string Key(XElement element) => element.Attribute(Xaml + "Key")!.Value;

    private static string? Colour(XElement theme, string key) =>
        theme.Elements(Presentation + "Color").SingleOrDefault(e => Key(e) == key)?.Value.Trim();

    private static string? Brush(XElement theme, string key) =>
        theme.Elements(Presentation + "SolidColorBrush").SingleOrDefault(e => Key(e) == key)?.Attribute("Color")?.Value;
}
