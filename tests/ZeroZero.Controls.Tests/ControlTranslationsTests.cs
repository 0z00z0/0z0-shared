using Xunit;

namespace ZeroZero.Controls.Tests;

/// <summary>The translator's files of the controls' own texts.</summary>
public class ControlTranslationsTests
{
    private static string File(string name) => Path.Combine(AppContext.BaseDirectory, "Translations", name);

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void TheNorwegianFileCarriesEveryKeyWithTheSamePlaceholders() =>
        TranslationFiles.AssertSameKeysAndPlaceholders(File("Texts.resx"), File("Texts.nb.resx"));

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("Texts.resx")]
    [InlineData("Texts.nb.resx")]
    public void EveryEntryTellsTheTranslatorWhatItIs(string name) =>
        TranslationFiles.AssertEveryEntryHasAComment(File(name));
}
