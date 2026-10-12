using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Jeffijoe.MessageFormat;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The module's texts: the host's localizer first and the module's own resource files under
/// it, and the wording rules both languages are held to.</summary>
public class MqttTextsTests
{
    private static readonly string NeutralPath = Path.Combine(AppContext.BaseDirectory, "Translations", "Texts.resx");
    private static readonly string NorwegianPath = Path.Combine(AppContext.BaseDirectory, "Translations", "Texts.nb.resx");

    private static readonly IReadOnlyDictionary<string, string> Neutral =
        TranslationFiles.Read(NeutralPath).ToDictionary(static e => e.Key, static e => e.Value.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> Norwegian =
        TranslationFiles.Read(NorwegianPath).ToDictionary(static e => e.Key, static e => e.Value.Value, StringComparer.Ordinal);

    private static string Text(FixedLocalizer host, string key) => new MqttPanelText(host).Strings[key].Value;

    // ------------------------------------------------------------------------------------------
    // The lookup order.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void AKeyTheHostDoesNotHoldFallsBackToTheModulesOwnText() =>
        Assert.Equal("just now", Text(new FixedLocalizer([]), "AgeJustNow"));

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void AHostThatAnswersOutranksTheModulesOwnText() =>
        Assert.Equal("gerade eben", Text(new FixedLocalizer(new() { ["AgeJustNow"] = "gerade eben" }), "AgeJustNow"));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankHostAnswerIsNoAnswer(string blank) =>
        // A resource left blank is one nobody has filled in, not an instruction to render nothing.
        Assert.Equal("just now", Text(new FixedLocalizer(new() { ["AgeJustNow"] = blank }), "AgeJustNow"));

    [Fact]
    public void AKeyNothingDeclaresRendersAsItself() =>
        // Visible in a screenshot rather than silently empty.
        Assert.Equal("NoSuchKey", MqttPanelText.Default.Strings["NoSuchKey"].Value);

    [Fact]
    public void TheHostIsAskedOncePerLookup()
    {
        var host = new FixedLocalizer([]);
        _ = Text(host, "AgeJustNow");

        Assert.Equal(1, host.Lookups);
    }

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("nb-NO", "Ingenting publisert ennå")]
    [InlineData("fr-FR", "Nothing published yet")]
    public void TheUiCulturePicksTheLanguageAndAnUnknownOneTakesEnGb(string culture, string expected) =>
        Assert.Equal(expected,
            UiCulture.Run(CultureInfo.GetCultureInfo(culture), () => MqttPanelText.Default.DescribeLastPublish(null, DateTimeOffset.UnixEpoch)));

    [Fact]
    public void ATranslationMayReorderItsPlaceholders()
    {
        // The whole reason composed text is a format string rather than a concatenation.
        var text = new MqttPanelText(new FixedLocalizer(new() { ["StatusBrokerInUse"] = "{2}: {0} port {1}" }));
        var request = new MqttEndpointRequest("broker.invalid", "user", 8883, MqttTransportMode.Tcp);

        Assert.Equal("TCP: broker.invalid port 8883", text.DescribeBroker(request, null));
    }

    // ------------------------------------------------------------------------------------------
    // The translator's files.
    // ------------------------------------------------------------------------------------------

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void TheNorwegianFileCarriesEveryKeyWithTheSamePlaceholders() =>
        TranslationFiles.AssertSameKeysAndPlaceholders(NeutralPath, NorwegianPath);

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("Texts.resx")]
    [InlineData("Texts.nb.resx")]
    public void EveryEntryTellsTheTranslatorWhatItIs(string file) =>
        TranslationFiles.AssertEveryEntryHasAComment(Path.Combine(AppContext.BaseDirectory, "Translations", file));

    [Fact]
    public void EveryTextFormatsInEveryLanguage()
    {
        // A malformed pattern throws where it is rendered, which is on screen rather than in a build.
        foreach (var (language, texts) in new[] { ("en-GB", Neutral), ("nb", Norwegian) })
            foreach (var (key, value) in texts)
            {
                var names = TranslationFiles.Placeholders(value);
                var exception = Record.Exception(() =>
                    _ = names.All(static n => n.All(char.IsAsciiDigit))
                        ? string.Format(CultureInfo.InvariantCulture, value, Enumerable.Range(0, 4).Cast<object?>().ToArray())
                        : new MessageFormatter(useCache: false, CultureInfo.GetCultureInfo(language))
                            .FormatMessage(value, names.ToDictionary(static n => n, static object? (_) => 2)));

                Assert.True(exception is null, $"{language} {key}: {exception?.Message}");
            }
    }

    [Fact]
    public void EveryResourceEntryKeepsItsWhitespace()
    {
        // Without xml:space the reader trims, which eats the blank lines a multi-paragraph warning is built from.
        foreach (var path in new[] { NeutralPath, NorwegianPath })
            foreach (var data in XDocument.Load(path).Root!.Elements("data"))
                Assert.Equal("preserve", data.Attribute(XNamespace.Xml + "space")?.Value);
    }

    [Fact]
    public void NoCompiledTextCarriesACarriageReturn()
    {
        // A literal line break in a resource file compiles as the checkout's line ending; a paragraph
        // break is written as a character reference instead.
        foreach (var culture in new[] { "en-GB", "nb-NO" })
            foreach (var text in UiCulture.Run(CultureInfo.GetCultureInfo(culture), () => MqttPanelText.Default.Strings.GetAllStrings(false).ToList()))
                Assert.DoesNotContain('\r', text.Value);
    }

    [Fact]
    public void NoKeyCarriesADotBecauseAResourceFileWouldReadItAsAPropertyName() =>
        // "Status.Age" in a .resw is the Age property of an element called Status, not one string.
        Assert.All(Neutral.Keys, k => Assert.DoesNotContain('.', k));

    // ------------------------------------------------------------------------------------------
    // Wording rules.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void EveryEntryHasText() =>
        Assert.All(Neutral, e => Assert.False(string.IsNullOrWhiteSpace(e.Value)));

    [Fact]
    public void NoStringAddressesTheReader()
    {
        // "Leave blank for anonymous access" is a statement about the field; "you can leave this
        // blank" is an instruction to a person. The second person is what the rule bans.
        foreach (var (texts, banned) in new[]
                 {
                     (Neutral, new[] { "you", "your", "yours" }),
                     (Norwegian, new[] { "du", "deg", "din", "ditt", "dine", "dere", "vi", "jeg" }),
                 })
            foreach (var (key, value) in texts)
                foreach (string word in banned)
                    Assert.False(Regex.IsMatch(value, $@"\b{word}\b", RegexOptions.IgnoreCase),
                                 $"{key} addresses the reader: {value}");
    }

    [Fact]
    public void EveryStringIsEnGb()
    {
        // The neutral language is en-GB, so an American spelling here is one that reaches a user.
        string[] american =
            ["color", "behavior", "customize", "initialize", "analyze", "recognize", "center ", "canceled"];

        foreach (var (key, value) in Neutral)
            foreach (string word in american)
                Assert.False(value.Contains(word, StringComparison.OrdinalIgnoreCase),
                             $"{key} is not en-GB: {value}");
    }

    [Fact]
    public void ARowWhoseDescriptionCouldOnlyRestateItsLabelHasNone()
    {
        // Host is the worked example: a description exists only where it adds something.
        Assert.False(Neutral.ContainsKey("DescHost"));
        Assert.True(Neutral.ContainsKey("RowHost"));
        Assert.True(Neutral.ContainsKey("InfoHost"));
    }

    [Fact]
    public void NoDescriptionRepeatsItsOwnLabel()
    {
        foreach (var texts in new[] { Neutral, Norwegian })
            foreach (var (key, description) in texts)
            {
                if (!key.StartsWith("Desc", StringComparison.Ordinal)) continue;
                if (!texts.TryGetValue("Row" + key[4..], out string? label)) continue;

                Assert.False(description.StartsWith(label, StringComparison.OrdinalIgnoreCase),
                             $"{key} opens by restating its label: {description}");
            }
    }

    [Fact]
    public void EveryRowWithAnInfoIconHasSomethingToSay()
    {
        foreach (var (key, value) in Neutral)
        {
            if (!key.StartsWith("Info", StringComparison.Ordinal)) continue;

            // An icon that opens on a fragment is worse than no icon.
            Assert.True(value.Length > 40, $"{key} is too short to be worth an icon: {value}");
        }
    }

    [Fact]
    public void NoStringNamesSomethingOnlyTheImplementationHas()
    {
        // The module's own machinery, which a user has no way to see and no reason to learn. Whole
        // words, so WebSocket keeps its own socket and a settings file keeps its fields.
        string[] banned =
        [
            "probe", "probes", "probed", "probing",
            "sweep", "sweeps", "sweeping", "swept",
            "coalesce", "coalesced", "coalescing", "channel", "channels",
            "stage", "stages", "staged", "staging",
            "debounce", "debounced", "debouncing", "settle", "settles", "settled",
            "socket", "sockets", "callback", "handler", "dispatch", "marshal", "mutex",
            "thread", "async", "enum", "struct", "instance", "singleton", "dedupe", "seam",
        ];

        foreach (var (key, value) in Neutral)
            foreach (string word in banned)
                Assert.False(
                    Regex.IsMatch(value, $@"\b{word}\b", RegexOptions.IgnoreCase),
                    $"{key} names the implementation rather than what a user sees: {value}");
    }
}
