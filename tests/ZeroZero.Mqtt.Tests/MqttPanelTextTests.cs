using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The four Status values, composed by the module from live state rather than set by a
/// host. Pure, and the instant is passed in, so the wording is pinned without a clock.</summary>
public class MqttPanelTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly MqttPanelText Text = MqttPanelText.Default;

    private static MqttEndpointRequest Request(
        string host = "broker.invalid", int? port = null,
        MqttTransportMode transport = MqttTransportMode.Auto,
        MqttEncryptionMode encryption = MqttEncryptionMode.Auto) =>
        new(host, "user", port, transport, encryption);

    // ------------------------------------------------------------------------------------------
    // Ages — the two values that must move while nothing happens.
    // ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(2 * 3600, "2 hours ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(2 * 86400, "2 days ago")]
    public void RelativeChoosesSingularAndPluralInCode(int secondsAgo, string expected) =>
        Assert.Equal(expected, Text.Relative(Now.AddSeconds(-secondsAgo), Now, "never"));

    [Fact]
    public void RelativeUsesSeparateKeysForTheSingularAndThePlural()
    {
        // Not "hour" plus an "s": a language whose plural is a different word has both to change.
        Assert.NotEqual(MqttStrings.Builtin["AgeHour"], MqttStrings.Builtin["AgeHours"]);
        Assert.NotEqual(MqttStrings.Builtin["AgeDay"], MqttStrings.Builtin["AgeDays"]);
    }

    [Fact]
    public void AnAgeMovesOnWithNothingHavingHappened()
    {
        var published = Now.AddSeconds(-30);

        Assert.Equal("just now", Text.DescribeLastPublish(published, Now));
        Assert.Equal("2 min ago", Text.DescribeLastPublish(published, Now.AddMinutes(2)));
    }

    [Fact]
    public void NothingPublishedAndNothingReceivedEachHaveTheirOwnWording()
    {
        Assert.Equal("Nothing published yet", Text.DescribeLastPublish(null, Now));
        Assert.Equal("Nothing received yet", Text.DescribeLastCommand(null, Now));
    }

    // ------------------------------------------------------------------------------------------
    // Connection — how the endpoint was arrived at, and what the link is doing meanwhile.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ConnectionSaysSoWhenThereIsNoHostToConnectTo() =>
        Assert.Equal("No broker host set",
            Text.Connection(Request(host: "  "), null, MqttConnectionState.Connected, probing: false));

    [Fact]
    public void ConnectionReportsAProbeInFlightRatherThanTheSettledAnswer()
    {
        var memory = new MqttEndpointMemory("broker.invalid", "user", 8883, MqttTransport.Tcp, true);

        Assert.Equal("Looking for the broker",
            Text.Connection(Request(), memory, MqttConnectionState.Connected, probing: true));
    }

    [Fact]
    public void ConnectionNamesHowTheEndpointWasArrivedAtOnceItIsConnected()
    {
        var memory = new MqttEndpointMemory("broker.invalid", "user", 8883, MqttTransport.Tcp, true);

        Assert.Equal("Automatically detected — encrypted",
            Text.Connection(Request(), memory, MqttConnectionState.Connected, probing: false));
    }

    [Fact]
    public void ConnectionSaysNotEncryptedWhenAutomaticSettledOnClearText()
    {
        // Automatic downgrades on its own, so nothing else on the page would say the link is plain.
        var memory = new MqttEndpointMemory("broker.invalid", "user", 1883, MqttTransport.Tcp, false);

        Assert.Equal("Automatically detected — not encrypted",
            Text.Connection(Request(), memory, MqttConnectionState.Connected, probing: false));
    }

    [Fact]
    public void ConnectionSaysSetManuallyOnlyWhenNothingIsLeftToFind()
    {
        var pinned = Request(port: 8883, transport: MqttTransportMode.Tcp, encryption: MqttEncryptionMode.On);
        var half = Request(port: 8883, transport: MqttTransportMode.Tcp);

        Assert.Equal("Set manually — encrypted",
            Text.Connection(pinned, null, MqttConnectionState.Connected, probing: false));
        Assert.StartsWith("Automatically detected",
            Text.Connection(half, null, MqttConnectionState.Connected, probing: false),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MqttConnectionState.Disabled, "Not publishing")]
    [InlineData(MqttConnectionState.Searching, "Looking for the broker")]
    [InlineData(MqttConnectionState.Connecting, "Connecting")]
    [InlineData(MqttConnectionState.Retrying, "Reconnecting")]
    [InlineData(MqttConnectionState.Failed, "Not connected")]
    public void ConnectionReportsWhatTheLinkIsDoingWhileNothingHasSettled(
        MqttConnectionState state, string expected) =>
        Assert.Equal(expected, Text.Connection(Request(), null, state, probing: false));

    // ------------------------------------------------------------------------------------------
    // The three surfaces that deliberately disagree.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void TheInstructionTheProvenanceAndTheAddressAreThreeDifferentAnswers()
    {
        // Automatic is a persisted choice meaning "find it each time". After a sweep the request
        // still says Auto, the Connection row says how it was arrived at, and Broker in use says
        // what it landed on. Collapsing any two would lose a fact.
        var request = Request();
        var memory = new MqttEndpointMemory("broker.invalid", "user", 8883, MqttTransport.Tcp, true);

        Assert.Equal(MqttTransportMode.Auto, request.Transport);
        Assert.Null(request.Port);
        Assert.Equal("Automatically detected — encrypted",
            Text.Connection(request, memory, MqttConnectionState.Connected, probing: false));
        Assert.Equal("broker.invalid:8883 over TCP", Text.DescribeBroker(request, memory));
    }

    [Fact]
    public void BrokerInUseWithholdsHalfAnAnswer() =>
        Assert.Equal("broker.invalid — not connected yet", Text.DescribeBroker(Request(), null));

    // ------------------------------------------------------------------------------------------
    // The sweep and its verdict.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void EveryProgressLineNamesWhatIsBeingTriedRatherThanACandidateIndex()
    {
        foreach (var stage in Enum.GetValues<MqttSearchStage>())
        {
            string line = Text.Describe(new MqttSearchProgress(
                stage, 8883, MqttTransport.WebSocket, new MqttProbeResult(MqttProbeOutcome.TimedOut, "")));

            Assert.Contains("WebSocket", line);
            Assert.Contains("8883", line);
        }
    }

    [Fact]
    public void EveryProbeOutcomeGetsItsOwnVerdict()
    {
        var sentences = Enum.GetValues<MqttProbeOutcome>()
            .Select(o => Text.Describe(new MqttProbeResult(o, "why"), MqttTransport.Tcp))
            .ToList();

        Assert.Equal(sentences.Count, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheOutcomesThatNeedDifferentUserResponsesReadDifferently()
    {
        string nothingListening = Text.Describe(
            new MqttProbeResult(MqttProbeOutcome.Unreachable, "nothing is listening on that port"),
            MqttTransport.Tcp);
        string refusedCredentials = Text.Describe(
            new MqttProbeResult(MqttProbeOutcome.AuthRejected, "NotAuthorised"), MqttTransport.Tcp);
        string tlsFailed = Text.Describe(
            new MqttProbeResult(MqttProbeOutcome.TlsUntrusted, "the remote certificate is invalid"),
            MqttTransport.Tcp);
        string timedOut = Text.Describe(
            new MqttProbeResult(MqttProbeOutcome.TimedOut, ""), MqttTransport.Tcp);
        string connected = Text.Describe(
            new MqttProbeResult(MqttProbeOutcome.Success, ""), MqttTransport.Tcp);

        Assert.Contains("nothing is listening", nothingListening);
        Assert.Contains("rejected these credentials", refusedCredentials);
        Assert.Contains("certificate trust", tlsFailed);
        Assert.DoesNotContain("credentials", tlsFailed);
        Assert.Contains("did not answer", timedOut);
        Assert.Contains("accepted these settings", connected);
    }

    [Fact]
    public void ATranslatedPanelComposesTheSameFactsFromItsOwnStrings()
    {
        var strings = new MqttStrings(new Fixed(new()
        {
            ["ProvenanceAutomatic"] = "Funnet automatisk",
            ["ProvenanceEncrypted"] = "{0} — kryptert",
        }));
        var translated = new MqttPanelText(strings);
        var memory = new MqttEndpointMemory("broker.invalid", "user", 8883, MqttTransport.Tcp, true);

        Assert.Equal("Funnet automatisk — kryptert",
            translated.Connection(Request(), memory, MqttConnectionState.Connected, probing: false));
        // Untranslated keys still answer, in the module's own en-GB.
        Assert.Equal("Nothing published yet", translated.DescribeLastPublish(null, Now));
    }

    private sealed class Fixed(Dictionary<string, string> entries) : IMqttStringSource
    {
        public string? Find(string key) => entries.GetValueOrDefault(key);
    }
}
