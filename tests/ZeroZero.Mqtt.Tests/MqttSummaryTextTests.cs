using System.Globalization;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>What a collapsed section says about itself. Composed by the module from live state, so
/// the wording is pinned without a window — the one thing a green build cannot otherwise check.
/// </summary>
public class MqttSummaryTextTests
{
    private static readonly MqttPanelText Text = MqttPanelText.Default;

    private static MqttEndpointRequest Request(
        string host = "broker.invalid", int? port = null,
        MqttTransportMode transport = MqttTransportMode.Auto,
        MqttEncryptionMode encryption = MqttEncryptionMode.Auto) =>
        new(host, "user", port, transport, encryption);

    private static MqttEndpointMemory Memory(
        int port = 8883, MqttTransport transport = MqttTransport.Tcp, bool? encrypted = true) =>
        new("broker.invalid", "user", port, transport, encrypted);

    private static string Summary(
        MqttEndpointRequest request, MqttEndpointMemory? memory = null,
        MqttConnectionState state = MqttConnectionState.Connected) =>
        Text.SummariseBroker(request, memory, state);

    // ------------------------------------------------------------------------------------------
    // The Broker section: what is configured, never what a sweep settled on.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ASummaryWithNoHostSaysSoRatherThanRenderingAnEmptyEndpoint() =>
        Assert.Equal("No broker set", Summary(Request(host: "   "), Memory()));

    [Fact]
    public void EveryFieldSetByHandIsShownAsItsOwnValueWithNothingMarked()
    {
        var request = Request(port: 8883, transport: MqttTransportMode.Tcp,
                              encryption: MqttEncryptionMode.On);

        Assert.Equal("broker.invalid · 8883 · TCP · encrypted", Summary(request, Memory()));
    }

    [Fact]
    public void AFieldLeftOnAutomaticShowsTheValueInForceMarkedAsDetected()
    {
        // The whole point of the marking: the value is real, and it was found rather than chosen.
        Assert.Equal("broker.invalid · 8883 (detected) · TCP (detected) · encrypted (detected)",
                     Summary(Request(), Memory()));
    }

    [Fact]
    public void PinnedAndAutomaticFieldsSitSideBySideEachSayingWhichItIs()
    {
        var request = Request(port: 1883);
        var memory = Memory(port: 1883, transport: MqttTransport.WebSocket, encrypted: false);

        Assert.Equal("broker.invalid · 1883 · WebSocket (detected) · not encrypted (detected)",
                     Summary(request, memory));
    }

    [Fact]
    public void AnAutomaticFieldWithNothingBehindItYetStandsAsTheBareInstruction()
    {
        // A fresh installation: Automatic everywhere and nothing has answered. An empty bracket
        // would read as a value that failed to render.
        Assert.Equal("broker.invalid · Automatic · Automatic · Automatic", Summary(Request()));
    }

    [Fact]
    public void ADetectedValueIsDroppedTheMomentTheLinkThatFoundItIsDown()
    {
        // Otherwise the summary shows the last successful sweep as though it were current, which is
        // the error the marking exists to prevent arriving by another route.
        foreach (var state in new[]
                 {
                     MqttConnectionState.Disabled, MqttConnectionState.Searching,
                     MqttConnectionState.Connecting, MqttConnectionState.Retrying,
                     MqttConnectionState.Failed,
                 })
            Assert.Equal("broker.invalid · Automatic · Automatic · Automatic",
                         Summary(Request(), Memory(), state));
    }

    [Fact]
    public void AMemoryFoundForAnotherHostSaysNothingAboutThisOne()
    {
        var elsewhere = new MqttEndpointMemory("other.invalid", "user", 1883, MqttTransport.Tcp, false);

        Assert.Equal("broker.invalid · Automatic · Automatic · Automatic",
                     Summary(Request(), elsewhere));
    }

    [Fact]
    public void AMemoryFoundForAnotherUserNameSaysNothingAboutThisOne()
    {
        var otherAccount = new MqttEndpointMemory("broker.invalid", "someone", 1883, MqttTransport.Tcp, false);

        Assert.Equal("broker.invalid · Automatic · Automatic · Automatic",
                     Summary(Request(), otherAccount));
    }

    [Fact]
    public void AnEntryPredatingTheRecordedSchemeCannotAnswerAnAutomaticEncryptionQuestion()
    {
        // Absent is not plain, and reading it as plain would show "not encrypted (detected)" on the
        // strength of a field nobody ever wrote.
        Assert.Equal("broker.invalid · Automatic · Automatic · Automatic",
                     Summary(Request(), Memory(encrypted: null)));
    }

    [Fact]
    public void AnAddressThatFixesTheSchemeOutranksAnExplicitChoiceOfPlain()
    {
        // A WebSocket front door on 443 is encrypted by its port whatever the switch says. Saying
        // otherwise would put a false statement about the wire beside a Status row asserting the
        // opposite — and it is not "detected", it is settled by the address, so it carries no mark.
        var request = Request(port: 443, transport: MqttTransportMode.WebSocket,
                              encryption: MqttEncryptionMode.Off);

        Assert.Equal("broker.invalid · 443 · WebSocket · encrypted", Summary(request, memory: null));
    }

    [Fact]
    public void ADetectedFrontDoorSettlesTheSchemeTheSameWayAPinnedOneDoes()
    {
        // Encryption switched off, the port left to be found, and 443 is what answered. The link is
        // encrypted by its address whatever the switch says, and the Connection row says so — a
        // summary reading "not encrypted" beside it would be the same contradiction the address rule
        // exists to prevent, reached through a found port rather than a typed one.
        var request = Request(transport: MqttTransportMode.WebSocket,
                              encryption: MqttEncryptionMode.Off);
        var memory = Memory(port: 443, transport: MqttTransport.WebSocket, encrypted: true);

        Assert.Equal("broker.invalid · 443 (detected) · WebSocket · encrypted", Summary(request, memory));
    }

    [Fact]
    public void WithTheLinkDownTheSchemeIsTheBareInstructionAgain()
    {
        // Nothing is connected, so no port has been found, nothing fixes the scheme, and no Status
        // row is asserting the opposite.
        var request = Request(transport: MqttTransportMode.WebSocket,
                              encryption: MqttEncryptionMode.Off);
        var memory = Memory(port: 443, transport: MqttTransport.WebSocket, encrypted: true);

        Assert.Equal("broker.invalid · Automatic · WebSocket · not encrypted",
                     Summary(request, memory, MqttConnectionState.Failed));
    }

    [Fact]
    public void AnExplicitChoiceIsReadFromTheSettingsRatherThanFromWhatWasFound()
    {
        // Nothing is being detected under an explicit choice, so nothing a sweep recorded may reach
        // the line — the instruction is the whole answer.
        var request = Request(port: 1883, transport: MqttTransportMode.Tcp,
                              encryption: MqttEncryptionMode.Off);

        Assert.Equal("broker.invalid · 1883 · TCP · not encrypted",
                     Summary(request, Memory(port: 1883, encrypted: true)));
    }

    [Fact]
    public void APinnedPortOutranksTheDetectedOneWhenTheAddressDecidesEncryption()
    {
        // The pinned and remembered ports differ, so which of the two settles the scheme is visible:
        // 1883 over WebSocket fixes nothing, and the 443 a sweep found must not reach the line.
        var request = Request(port: 1883, transport: MqttTransportMode.WebSocket,
                              encryption: MqttEncryptionMode.Off);
        var memory = Memory(port: 443, transport: MqttTransport.WebSocket, encrypted: true);

        Assert.Equal("broker.invalid · 1883 · WebSocket · not encrypted", Summary(request, memory));
    }

    [Fact]
    public void ThePortDoesNotVaryWithTheDisplayCulture()
    {
        // A port is an address, not a quantity: a rendering that grouped four digits would put a
        // separator inside it. The publish count is deliberately not asserted here — a count is a
        // quantity, and the current culture is exactly what it should render for.
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "nb-NO", "fr-CH", "hi-IN", "ar-EG" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);

                Assert.Equal("broker.invalid · 8883 (detected) · TCP (detected) · encrypted (detected)",
                             Summary(Request(), Memory()));
                Assert.Equal("broker.invalid · 1883 · TCP · encrypted",
                             Summary(Request(port: 1883, transport: MqttTransportMode.Tcp,
                                             encryption: MqttEncryptionMode.On), Memory(port: 1883)));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void TheSummaryIsNotTheStatusRowRestated()
    {
        // Two sections apart, and answering different questions: the Status row carries the endpoint
        // in force, this one carries what the settings ask for.
        var request = Request();
        var memory = Memory();

        Assert.NotEqual(Text.DescribeBroker(request, memory), Summary(request, memory));
    }

    [Fact]
    public void ATranslationMayReorderTheSummarysParts()
    {
        var strings = new MqttStrings(new Fixed(new() { ["SummaryBroker"] = "{2} {3} {0}:{1}" }));
        var request = Request(port: 8883, transport: MqttTransportMode.Tcp,
                              encryption: MqttEncryptionMode.On);

        Assert.Equal("TCP encrypted broker.invalid:8883",
                     new MqttPanelText(strings).SummariseBroker(request, null, MqttConnectionState.Connected));
    }

    // ------------------------------------------------------------------------------------------
    // The publish section: a count, because the module declares no group of its own.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ThePublishSummaryCountsTheGroupsThatAreOnAgainstTheGroupsThereAre() =>
        Assert.Equal("2 of 3 switched on", Text.SummarisePublish(new MqttPublishTally(2, 3)));

    [Fact]
    public void EveryGroupOffIsStillACountRatherThanAClaimThatNothingIsPublished() =>
        // Entities carrying no group key publish regardless, so "nothing published" would be false.
        Assert.Equal("0 of 3 switched on", Text.SummarisePublish(new MqttPublishTally(0, 3)));

    [Fact]
    public void AConsumerDeclaringNoGroupsGetsASentenceRatherThanZeroOfZero() =>
        Assert.Equal("Nothing to switch on or off", Text.SummarisePublish(new MqttPublishTally(0, 0)));

    [Fact]
    public void TheTallyCountsDeclaredGroupsAgainstTheStateStoredForThem()
    {
        var store = new RecordingSettingsStore();
        var groups = new PublishGroupSet(store,
        [
            new("state", "State"),
            new("metrics", "Metrics", DefaultOn: false),
            new("controls", "Controls"),
        ]);

        // Untouched: the declared defaults stand, two on and one off.
        Assert.Equal(new MqttPublishTally(2, 3), MqttPublishRows.Tally(groups));

        groups.Set("metrics", true);
        Assert.Equal(new MqttPublishTally(3, 3), MqttPublishRows.Tally(groups));

        groups.Set("state", false);
        Assert.Equal(new MqttPublishTally(2, 3), MqttPublishRows.Tally(groups));
    }

    [Fact]
    public void TheTallyOfNoDeclaredGroupsIsZeroOfZeroRatherThanAThrow() =>
        Assert.Equal(new MqttPublishTally(0, 0),
                     MqttPublishRows.Tally(new PublishGroupSet(new RecordingSettingsStore(), [])));

    // ------------------------------------------------------------------------------------------
    // The static facade, which is what a panel with no string source of its own calls.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void TheStaticFacadeComposesTheSameTwoLines()
    {
        Assert.Equal("broker.invalid · 8883 (detected) · TCP (detected) · encrypted (detected)",
                     MqttStatusText.SummariseBroker(Request(), Memory(), MqttConnectionState.Connected));
        Assert.Equal("1 of 2 switched on", MqttStatusText.SummarisePublish(new MqttPublishTally(1, 2)));
    }

    private sealed class Fixed(Dictionary<string, string> entries) : IMqttStringSource
    {
        public string? Find(string key) => entries.GetValueOrDefault(key);
    }
}
