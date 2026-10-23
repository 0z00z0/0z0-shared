using System.Globalization;
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
    [InlineData(60, "1 minute ago")]
    [InlineData(5 * 60, "5 minutes ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(2 * 3600, "2 hours ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(2 * 86400, "2 days ago")]
    public void RelativeChoosesSingularAndPluralByThePluralRule(int secondsAgo, string expected) =>
        Assert.Equal(expected, Text.Relative(Now.AddSeconds(-secondsAgo), Now, "never"));

    /// <summary>The last second each unit still owns. A coarser unit reached one second early renders
    /// a count of zero — "0 hours ago" for an age of fifty-nine minutes — which reads as a broken value
    /// rather than as an age, and the value at the boundary itself cannot see it.</summary>
    [Theory]
    [InlineData(3599, "59 minutes ago")]
    [InlineData(86399, "23 hours ago")]
    public void RelativeKeepsAUnitUntilTheNextOneIsWhole(int secondsAgo, string expected) =>
        Assert.Equal(expected, Text.Relative(Now.AddSeconds(-secondsAgo), Now, "never"));

    /// <summary>InvariantGlobalization leaves every UI culture invariant, which has no plural rule of
    /// its own; the neutral language's rule stands in, or one would read "1 hours ago".</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void UnderTheInvariantCultureOneIsStillSingular() =>
        Assert.Equal("1 hour ago",
            UiCulture.Run(CultureInfo.InvariantCulture, () => Text.Relative(Now.AddHours(-1), Now, "never")));

    [Theory]
    [InlineData(3600, "for 1 time siden")]
    [InlineData(2 * 3600, "for 2 timer siden")]
    public void ANorwegianUiCultureTakesTheBokmalForms(int secondsAgo, string expected) =>
        Assert.Equal(expected,
            UiCulture.Run(CultureInfo.GetCultureInfo("nb-NO"), () => Text.Relative(Now.AddSeconds(-secondsAgo), Now, "never")));

    [Fact]
    public void AnAgeMovesOnWithNothingHavingHappened()
    {
        var published = Now.AddSeconds(-30);

        Assert.Equal("just now", Text.DescribeLastPublish(published, Now));
        Assert.Equal("2 minutes ago", Text.DescribeLastPublish(published, Now.AddMinutes(2)));
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
        Assert.Contains("encrypted connection", tlsFailed);
        Assert.DoesNotContain("credentials", tlsFailed);
        Assert.Contains("did not answer", timedOut);
        Assert.Equal("Connected over TCP.", connected);
    }

    // ------------------------------------------------------------------------------------------
    // The run verdict: the outcome, never the attempts behind it.
    // ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(MqttProbeOutcome.Unreachable)]
    [InlineData(MqttProbeOutcome.TimedOut)]
    [InlineData(MqttProbeOutcome.TlsUnsupported)]
    [InlineData(MqttProbeOutcome.Failed)]
    public void ASuccessCarriesNothingAboutAnAttemptThatFailedOnTheWay(MqttProbeOutcome failed)
    {
        MqttProbeReport report = new([
            new(new(1883, MqttTransport.Tcp), new MqttProbeResult(failed, "nothing answered on that port")),
            new(new(443, MqttTransport.WebSocket), MqttProbeOutcome.Success),
        ]);

        string sentence = Text.Describe(report);

        // The defect this pins: a failed leg printed under a success reads as a finding, and a user
        // scanning it concludes something is wrong with a connection that works.
        Assert.Equal("Connected over WebSocket on port 443.", sentence);
        Assert.DoesNotContain("TCP", sentence);
        Assert.DoesNotContain("nothing answered", sentence);
    }

    [Fact]
    public void ASuccessNamesTheEndpointItConnectedOnRatherThanOnlyTheTransport()
    {
        MqttProbeReport report = new([new(new(8883, MqttTransport.Tcp), MqttProbeOutcome.Success)]);

        Assert.Equal("Connected over TCP on port 8883.", Text.Describe(report));
    }

    [Fact]
    public void ATotalFailureStillSaysWhatWasTriedAndWhatEachDid()
    {
        MqttProbeReport report = new([
            new(new(1883, MqttTransport.Tcp), new MqttProbeResult(MqttProbeOutcome.Unreachable, "nothing is listening on that port")),
            new(new(443, MqttTransport.WebSocket), MqttProbeOutcome.TimedOut),
        ]);

        string sentence = Text.Describe(report);

        // The mirror of the rule above: with nothing to report as an outcome, the attempts are what
        // a reader has to go on, so they must survive.
        Assert.Contains("TCP", sentence);
        Assert.Contains("nothing is listening on that port", sentence);
        Assert.Contains("WebSocket", sentence);
        Assert.Contains("did not answer", sentence);
    }

    [Fact]
    public void ABrokerThatAnsweredAndRefusedIsItsOwnWholeAnswer()
    {
        MqttProbeReport report = new([
            new(new(1883, MqttTransport.Tcp), MqttProbeOutcome.Unreachable),
            new(new(443, MqttTransport.WebSocket), new MqttProbeResult(MqttProbeOutcome.AuthRejected, "NotAuthorised")),
        ]);

        string sentence = Text.Describe(report);

        // A refused credential is the finding; naming the transport that never answered sends nobody
        // anywhere useful.
        Assert.Equal("The broker rejected these credentials over WebSocket (NotAuthorised).", sentence);
    }

    [Fact]
    public void ATranslatedPanelComposesTheSameFactsFromItsOwnStrings()
    {
        var strings = new FixedLocalizer(new()
        {
            ["ProvenanceAutomatic"] = "Automatisch gefunden",
            ["ProvenanceEncrypted"] = "{0} — verschlüsselt",
        });
        var translated = new MqttPanelText(strings);
        var memory = new MqttEndpointMemory("broker.invalid", "user", 8883, MqttTransport.Tcp, true);

        Assert.Equal("Automatisch gefunden — verschlüsselt",
            translated.Connection(Request(), memory, MqttConnectionState.Connected, probing: false));
        // Keys the host does not hold still answer, in the module's own text.
        Assert.Equal("Nothing published yet", translated.DescribeLastPublish(null, Now));
    }

    // ------------------------------------------------------------------------------------------
    // Edges of each line.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Relative_SaysSoWhenThereIsNothingToRender() =>
        Assert.Equal("never", Text.Relative(null, Now, never: "never"));

    [Fact]
    public void Relative_ReadsAFutureStampAsJustNowRatherThanANegativeAge() =>
        Assert.Equal("just now", Text.Relative(Now.AddHours(1), Now, "never"));

    [Fact]
    public void DescribeBroker_SaysNotSetWithNoHost() =>
        Assert.Equal("Not set", Text.DescribeBroker(
            new("  ", "user", null, MqttTransportMode.Auto), null));

    [Fact]
    public void DescribeBroker_IgnoresAnEndpointFoundSomewhereElse()
    {
        var request = new MqttEndpointRequest("broker.invalid", "user", null, MqttTransportMode.Auto);
        var elsewhere = new MqttEndpointMemory("other.invalid", "user", 8883, MqttTransport.Tcp, true);

        Assert.Equal("broker.invalid — not connected yet", Text.DescribeBroker(request, elsewhere));
    }

    [Fact]
    public void DescribeLastCommand_FallsBackToTheEntityIdTheWireCarried()
    {
        var record = new MqttCommandRecord(Now.AddMinutes(-5), "quiet_mode");

        Assert.Equal("quiet_mode — 5 minutes ago", Text.DescribeLastCommand(record, Now));
        Assert.Equal("Quiet mode — 5 minutes ago",
            Text.DescribeLastCommand(record, Now, _ => "Quiet mode"));
    }

    [Fact]
    public void Describe_NamesTheTransportInEveryVerdict()
    {
        foreach (var outcome in Enum.GetValues<MqttProbeOutcome>())
        {
            string sentence = Text.Describe(new(outcome, "why"), MqttTransport.WebSocket);

            Assert.Contains("WebSocket", sentence);
        }
    }

    [Fact]
    public void Describe_OfACertificateFailurePointsAtNoControlThePanelDoesNotCarry()
    {
        string sentence = Text.Describe(
            new(MqttProbeOutcome.TlsUntrusted, "the remote certificate is invalid"), MqttTransport.Tcp);

        // The reason the far end gave, and nothing sending a reader to look for a certificate-trust
        // field: the panel has none, and trust is configured by the host in code.
        Assert.Contains("the remote certificate is invalid", sentence);
        Assert.DoesNotContain("credentials", sentence);
        Assert.DoesNotContain("setting", sentence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_OfAProgressLineTellsTheTwoStagesApart()
    {
        string port = Text.Describe(
            new MqttSearchProgress(MqttSearchStage.Port, 1883, MqttTransport.Tcp));
        string transport = Text.Describe(
            new MqttSearchProgress(MqttSearchStage.Transport, 1883, MqttTransport.Tcp));

        Assert.Equal("Trying TCP on port 1883…", port);
        Assert.NotEqual(port, transport);
        Assert.Contains("asking the broker", transport);
    }

    [Fact]
    public void Describe_OfAFinishedStageWithNoResultDoesNotReadAsASuccess()
    {
        string sentence = Text.Describe(
            new MqttSearchProgress(MqttSearchStage.Finished, 1883, MqttTransport.Tcp));

        Assert.DoesNotContain("connected", sentence);
    }

    [Fact]
    public void Describe_OfAnEmptyRunSaysThereWasNothingToTry() =>
        Assert.Equal("No broker host set.", Text.Describe(new MqttProbeReport([])));

    [Fact]
    public void Describe_OfAFailedRunListsWhatWasTriedWithoutRepeatingItself()
    {
        MqttProbeReport report = new([
            new(new(1883, MqttTransport.Tcp), MqttProbeOutcome.TimedOut),
            new(new(8883, MqttTransport.Tcp), MqttProbeOutcome.TimedOut),
            new(new(443, MqttTransport.WebSocket), MqttProbeOutcome.TimedOut),
        ]);

        string sentence = Text.Describe(report);

        // Nothing answered anywhere, which is the one case where what was tried is the finding.
        Assert.Equal("The broker was not reached. TCP did not answer; WebSocket did not answer.",
            sentence);
    }

    [Fact]
    public void IsFailure_IsTrueForAnythingButASuccessfulRun()
    {
        Assert.False(MqttPanelText.IsFailure(
            new([new(new(1883, MqttTransport.Tcp), MqttProbeOutcome.Success)])));
        Assert.True(MqttPanelText.IsFailure(
            new([new(new(1883, MqttTransport.Tcp), MqttProbeOutcome.TlsUntrusted)])));
    }

    [Fact]
    public void Name_GivesEveryConnectionStateItsOwnWording()
    {
        var wordings = Enum.GetValues<MqttConnectionState>().Select(Text.Name).ToList();

        Assert.All(wordings, w => Assert.False(string.IsNullOrWhiteSpace(w)));
        Assert.Equal(wordings.Count, wordings.Distinct(StringComparer.Ordinal).Count());
    }
}
