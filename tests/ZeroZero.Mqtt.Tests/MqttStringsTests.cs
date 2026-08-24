using System.Globalization;
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
}
