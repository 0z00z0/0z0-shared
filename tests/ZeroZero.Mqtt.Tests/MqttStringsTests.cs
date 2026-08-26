using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>A translation layer whose failure mode must be the module's own en-GB rather than a
/// blank control.</summary>
public class MqttStringsTests
{
    private sealed class Source(Dictionary<string, string> entries) : IMqttStringSource
    {
        public int Lookups { get; private set; }

        public string? Find(string key)
        {
            Lookups++;
            return entries.GetValueOrDefault(key);
        }
    }

    [Fact]
    public void AKeyNoSourceAnswersFallsBackToTheModulesOwnText()
    {
        var strings = new MqttStrings(new Source([]));

        Assert.Equal("just now", strings.Get("AgeJustNow"));
    }

    [Fact]
    public void ASourceThatAnswersOutranksTheBuiltInText()
    {
        var strings = new MqttStrings(new Source(new() { ["AgeJustNow"] = "nettopp nå" }));

        Assert.Equal("nettopp nå", strings.Get("AgeJustNow"));
    }

    [Fact]
    public void AnEmptyTranslationIsNoTranslation()
    {
        // A resource left blank is a resource nobody has filled in, not an instruction to render
        // nothing.
        var strings = new MqttStrings(new Source(new() { ["AgeJustNow"] = "" }));

        Assert.Equal("just now", strings.Get("AgeJustNow"));
    }

    [Fact]
    public void AKeyNothingDeclaresRendersAsItself()
    {
        // Visible in a screenshot rather than silently empty.
        Assert.Equal("NoSuchKey", MqttStrings.Default.Get("NoSuchKey"));
    }

    [Fact]
    public void NoSourceIsAskedWhenThereIsNoneToAsk()
    {
        var source = new Source([]);
        _ = new MqttStrings(source).Get("AgeJustNow");

        Assert.Equal(1, source.Lookups);
        // The default instance carries no source at all, so it cannot depend on one loading.
        Assert.Equal("just now", MqttStrings.Default.Get("AgeJustNow"));
    }

    [Fact]
    public void FormatFillsPlaceholdersForTheCurrentCulture()
    {
        var strings = new MqttStrings(new Source(new() { ["Sample"] = "{0} of {1}" }));

        Assert.Equal("3 of 4", strings.Format("Sample", 3, 4));
    }

    [Fact]
    public void ATranslationMayReorderItsPlaceholders()
    {
        // The whole reason composed text is a format string rather than a concatenation.
        var strings = new MqttStrings(new Source(new() { ["StatusBrokerInUse"] = "{2}: {0} port {1}" }));
        var text = new MqttPanelText(strings);
        var request = new MqttEndpointRequest("broker.invalid", "user", 8883, MqttTransportMode.Tcp);

        Assert.Equal("TCP: broker.invalid port 8883", text.DescribeBroker(request, null));
    }

    [Fact]
    public void EveryBuiltInEntryHasText()
    {
        Assert.NotEmpty(MqttStrings.Builtin);
        Assert.All(MqttStrings.Builtin, e => Assert.False(string.IsNullOrWhiteSpace(e.Value)));
    }

    [Fact]
    public void NoKeyCarriesADotBecauseAResourceFileWouldReadItAsAPropertyName()
    {
        // "Status.Age" in a .resw is the Age property of an element called Status, not one string.
        Assert.All(MqttStrings.Builtin.Keys, k => Assert.DoesNotContain('.', k));
    }

    [Fact]
    public void NoStringAddressesTheReader()
    {
        // "You can leave this blank" is an instruction to a person; "Leave blank for anonymous
        // access" is a statement about the field. The second person is what the rule bans.
        string[] banned = ["you ", "your ", "you'", " yours"];

        foreach (var (key, value) in MqttStrings.Builtin)
            foreach (string word in banned)
                Assert.False(value.Contains(word, StringComparison.OrdinalIgnoreCase),
                             $"{key} addresses the reader: {value}");
    }

    [Fact]
    public void EveryStringIsEnGb()
    {
        // The neutral language is en-GB and this is the module's whole user-facing surface, so an
        // American spelling here is one that reaches a user.
        string[] american =
            ["color", "behavior", "customize", "initialize", "analyze", "recognize", "center ", "canceled"];

        foreach (var (key, value) in MqttStrings.Builtin)
            foreach (string word in american)
                Assert.False(value.Contains(word, StringComparison.OrdinalIgnoreCase),
                             $"{key} is not en-GB: {value}");
    }

    [Fact]
    public void ARowWhoseDescriptionCouldOnlyRestateItsLabelHasNone()
    {
        // Host is the worked example: "the broker's host name or address" is words without
        // information, and the row reads better without one. The rule is that a description exists
        // only where it adds something, so its absence is the assertion.
        Assert.False(MqttStrings.Builtin.ContainsKey("DescHost"));
        Assert.True(MqttStrings.Builtin.ContainsKey("RowHost"));
        Assert.True(MqttStrings.Builtin.ContainsKey("InfoHost"));
    }

    [Fact]
    public void NoDescriptionRepeatsItsOwnLabel()
    {
        foreach (var (key, description) in MqttStrings.Builtin)
        {
            if (!key.StartsWith("Desc", StringComparison.Ordinal)) continue;
            if (!MqttStrings.Builtin.TryGetValue("Row" + key[4..], out string? label)) continue;

            Assert.False(description.StartsWith(label, StringComparison.OrdinalIgnoreCase),
                         $"{key} opens by restating its label: {description}");
        }
    }

    [Fact]
    public void EveryRowWithAnInfoIconHasSomethingToSay()
    {
        foreach (var (key, value) in MqttStrings.Builtin)
        {
            if (!key.StartsWith("Info", StringComparison.Ordinal)) continue;

            // An icon that opens on a fragment is worse than no icon; the module's own always
            // carries at least a sentence.
            Assert.True(value.Length > 40, $"{key} is too short to be worth an icon: {value}");
        }
    }

    [Fact]
    public void EveryPlaceholderInABuiltInEntryIsWellFormed()
    {
        // A format string with an unbalanced brace throws at the point it is rendered, which is on
        // screen rather than in a build.
        foreach (var (key, value) in MqttStrings.Builtin)
        {
            var arguments = Enumerable.Range(0, 4).Cast<object?>().ToArray();
            var exception = Record.Exception(
                () => string.Format(CultureInfo.InvariantCulture, value, arguments));

            Assert.True(exception is null, $"{key}: {exception?.Message}");
        }
    }

    [Fact]
    public void NoStringNamesSomethingOnlyTheImplementationHas()
    {
        // Host, port, transport, broker and topic are protocol words a user meets in the wild and
        // are not here. These are the module's own machinery, which a user has no way to see and no
        // reason to learn — plus "staged", which is borrowed from version control and describes
        // neither. Whole words, so WebSocket keeps its own socket and a settings file keeps its
        // fields.
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

        foreach (var (key, value) in MqttStrings.Builtin)
            foreach (string word in banned)
                Assert.False(
                    Regex.IsMatch(value, $@"\b{word}\b", RegexOptions.IgnoreCase),
                    $"{key} names the implementation rather than what a user sees: {value}");
    }

    // ------------------------------------------------------------------------------------------
    // The panel's resource file against the built-in text.
    // ------------------------------------------------------------------------------------------

    /// <summary>The panel's <c>.resw</c>, read as data. It lives in a Windows-only project this one
    /// cannot reference, so the copy in the output directory is what the comparison reads.</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Resw = new(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources.resw");
        Assert.True(File.Exists(path), $"The panel's resource file was not copied to {path}.");

        return XDocument.Load(path).Root!.Elements("data").ToDictionary(
            d => d.Attribute("name")!.Value,
            d => d.Element("value")?.Value ?? "",
            StringComparer.Ordinal);
    });

    [Fact]
    public void TheResourceFileDeclaresExactlyTheKeysTheModuleDoes()
    {
        // A key in one and not the other is a string that renders as its own key on screen, or a
        // translator's entry nothing will ever ask for.
        var missing = MqttStrings.Builtin.Keys.Except(Resw.Value.Keys, StringComparer.Ordinal);
        var orphaned = Resw.Value.Keys.Except(MqttStrings.Builtin.Keys, StringComparer.Ordinal);

        Assert.Equal("", string.Join(", ", missing));
        Assert.Equal("", string.Join(", ", orphaned));
    }

    [Fact]
    public void TheResourceFileCarriesTheSameTextAsTheModule()
    {
        // The failure this exists for: a string reworded in one place and left standing in the
        // other. Both compile, every other test passes, and the panel shows the stale wording —
        // because the resource file outranks the built-in text wherever it answers.
        foreach (var (key, text) in MqttStrings.Builtin)
        {
            if (!Resw.Value.TryGetValue(key, out string? resource)) continue;

            Assert.True(string.Equals(text, resource, StringComparison.Ordinal),
                        $"{key} differs.\n  built-in: {text}\n  resource: {resource}");
        }
    }

    [Fact]
    public void EveryResourceEntryKeepsItsWhitespace()
    {
        // Without xml:space the reader trims, which silently eats the blank lines a multi-paragraph
        // warning is built from.
        foreach (var data in XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Resources.resw"))
                                      .Root!.Elements("data"))
            Assert.Equal("preserve", data.Attribute(XNamespace.Xml + "space")?.Value);
    }
}
