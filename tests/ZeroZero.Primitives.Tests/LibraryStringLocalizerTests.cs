using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
using Xunit;
using ZeroZero.Primitives;

namespace ZeroZero.Primitives.Tests;

/// <summary>The lookup order every component's texts follow: the host's localizer, then the
/// component's own resource files for the UI culture, then their neutral en-GB.</summary>
public class LibraryStringLocalizerTests
{
    private static readonly ResourceManager Sample =
        new("ZeroZero.Primitives.Tests.SampleTexts", typeof(LibraryStringLocalizerTests).Assembly);

    private sealed class Host(Dictionary<string, string> entries) : IStringLocalizer
    {
        public LocalizedString this[string name] =>
            entries.TryGetValue(name, out var value) ? new(name, value) : new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private static string In(string culture, Func<string> read) => UiCulture.Run(CultureInfo.GetCultureInfo(culture), read);

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void TheHostsAnswerOutranksTheComponentsOwn()
    {
        var texts = new LibraryStringLocalizer(Sample, new Host(new() { ["Greeting"] = "Howdy" }));

        Assert.Equal("Howdy", In("en-GB", () => texts["Greeting"]));
    }

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankHostAnswerIsNoAnswer(string blank)
    {
        var texts = new LibraryStringLocalizer(Sample, new Host(new() { ["Greeting"] = blank }));

        Assert.Equal("Hello", In("en-GB", () => texts["Greeting"]));
    }

    [Fact]
    public void AKeyTheHostDoesNotHoldFallsBackToTheComponent()
    {
        var texts = new LibraryStringLocalizer(Sample, new Host([]));

        Assert.Equal("Hello", In("en-GB", () => texts["Greeting"]));
    }

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("nb-NO", "Hei")]
    [InlineData("nb", "Hei")]
    [InlineData("fr-FR", "Hello")]
    [InlineData("en-US", "Hello")]
    public void TheUiCulturePicksTheLanguageAndAnUnknownOneTakesTheNeutral(string culture, string expected)
    {
        var texts = new LibraryStringLocalizer(Sample);

        Assert.Equal(expected, In(culture, () => texts["Greeting"]));
    }

    [Fact]
    public void AKeyNothingDeclaresRendersAsItselfAndSaysSo()
    {
        var text = new LibraryStringLocalizer(Sample)["NoSuchKey"];

        Assert.Equal("NoSuchKey", text.Value);
        Assert.True(text.ResourceNotFound);
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void TheUiCulturePicksTheWordsAndTheRegionalFormatPicksTheNumber()
    {
        var texts = new LibraryStringLocalizer(Sample);
        string Amount(string language, CultureInfo format) =>
            UiCulture.Run(CultureInfo.GetCultureInfo(language), format, () => texts["Amount", 1.5]);

        Assert.Equal("1.5 igjen", Amount("nb-NO", CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal("1,5 left", Amount("en-GB", CultureInfo.GetCultureInfo("nb-NO")));
        Assert.Equal("1,5 left", Amount("en-GB", UiCulture.EnGbWithDecimalComma()));
    }
}
