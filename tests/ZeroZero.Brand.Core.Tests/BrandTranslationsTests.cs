using Xunit;

namespace ZeroZero.Brand.Core.Tests;

/// <summary>The translator's files of the console banner and the About surface.</summary>
public class BrandTranslationsTests
{
    private static string File(string assembly, string name) =>
        Path.Combine(AppContext.BaseDirectory, "Translations", assembly, name);

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("Core")]
    [InlineData("WinUI")]
    public void TheNorwegianFileCarriesEveryKeyWithTheSamePlaceholders(string assembly) =>
        TranslationFiles.AssertSameKeysAndPlaceholders(File(assembly, "Texts.resx"), File(assembly, "Texts.nb.resx"));

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("Core", "Texts.resx")]
    [InlineData("Core", "Texts.nb.resx")]
    [InlineData("WinUI", "Texts.resx")]
    [InlineData("WinUI", "Texts.nb.resx")]
    public void EveryEntryTellsTheTranslatorWhatItIs(string assembly, string name) =>
        TranslationFiles.AssertEveryEntryHasAComment(File(assembly, name));
}
