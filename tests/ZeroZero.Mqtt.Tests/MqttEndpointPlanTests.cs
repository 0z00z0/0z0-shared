using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The sweep order, what an explicit choice pins, and when Automatic may fall back to clear
/// text. All pure, so none of it needs a socket.</summary>
public class MqttEndpointPlanTests
{
    private static MqttEndpointRequest Request(
        int? port = null,
        MqttTransportMode transport = MqttTransportMode.Auto,
        MqttEncryptionMode encryption = MqttEncryptionMode.Auto) =>
        new("broker.invalid", "user", port, transport, encryption);

    [Fact]
    public void Order_LeadsWithTcpFromACleanSlate() =>
        Assert.Equal([MqttTransport.Tcp, MqttTransport.WebSocket],
            MqttEndpointPlan.Order(MqttTransportMode.Auto, null));

    [Fact]
    public void Order_LeadsWithWhateverWorkedLastTime() =>
        Assert.Equal([MqttTransport.WebSocket, MqttTransport.Tcp],
            MqttEndpointPlan.Order(MqttTransportMode.Auto, MqttTransport.WebSocket));

    [Fact]
    public void Order_HasNoFallbackUnderAnExplicitChoice() =>
        Assert.Equal([MqttTransport.Tcp],
            MqttEndpointPlan.Order(MqttTransportMode.Tcp, MqttTransport.WebSocket));

    [Fact]
    public void OfferedPorts_AreExactlyThePortsTheSweepTries() =>
        Assert.Equal(
            [.. MqttEndpointPlan.Ports(MqttTransport.Tcp), .. MqttEndpointPlan.Ports(MqttTransport.WebSocket)],
            MqttEndpointPlan.OfferedPorts);

    [Fact]
    public void EncryptionOrder_AsksForCipherFirstUnderAutomatic() =>
        Assert.Equal([true, false], MqttEndpointPlan.EncryptionOrder(MqttEncryptionMode.Auto));

    [Fact]
    public void EncryptionOrder_HasNoFallbackUnderAnExplicitChoice()
    {
        Assert.Equal([true], MqttEndpointPlan.EncryptionOrder(MqttEncryptionMode.On));
        Assert.Equal([false], MqttEndpointPlan.EncryptionOrder(MqttEncryptionMode.Off));
    }

    [Fact]
    public void Sweep_TriesBothEncryptionsOnAPortBeforeMovingOn()
    {
        var sweep = MqttEndpointPlan.Sweep(Request(port: 1883, transport: MqttTransportMode.Tcp), null);

        Assert.Equal([new(1883, MqttTransport.Tcp, true), new(1883, MqttTransport.Tcp, false)], sweep);
    }

    [Fact]
    public void Sweep_LeadsWithTheRememberedEndpoint()
    {
        var memory = new MqttEndpointMemory("broker.invalid", "user", 8883, MqttTransport.Tcp, true);

        var sweep = MqttEndpointPlan.Sweep(Request(), memory);

        Assert.Equal(new MqttEndpointCandidate(8883, MqttTransport.Tcp, true), sweep[0]);
        Assert.True(sweep.Count > 1, "the remembered endpoint must lead the sweep, never replace it");
    }

    [Fact]
    public void Sweep_CollapsesTheEncryptionPairWhereThePortFixesTheScheme()
    {
        var sweep = MqttEndpointPlan.Sweep(Request(port: 443, transport: MqttTransportMode.WebSocket), null);

        Assert.Equal([new(443, MqttTransport.WebSocket, true)], sweep);
    }

    [Fact]
    public void Sweep_HonoursAPinnedPortEvenAgainstTheRememberedEndpoint()
    {
        var memory = new MqttEndpointMemory("broker.invalid", "user", 8883, MqttTransport.Tcp, true);

        var sweep = MqttEndpointPlan.Sweep(Request(port: 1883, transport: MqttTransportMode.Tcp), memory);

        Assert.All(sweep, c => Assert.Equal(1883, c.Port));
    }

    [Fact]
    public void Reusable_IgnoresAnEntryFoundForAnotherHostOrUser()
    {
        var elsewhere = new MqttEndpointMemory("other.invalid", "user", 1883, MqttTransport.Tcp, false);
        var otherUser = new MqttEndpointMemory("broker.invalid", "someone", 1883, MqttTransport.Tcp, false);

        Assert.Null(MqttEndpointPlan.Reusable(Request(), elsewhere));
        Assert.Null(MqttEndpointPlan.Reusable(Request(), otherUser));
    }

    [Fact]
    public void Reusable_IgnoresAnEntryThatDoesNotSayWhetherItWasEncrypted()
    {
        // Absent is not the same as plain: reading it as plain would pin Automatic to clear text for
        // good on the strength of a default.
        var incomplete = new MqttEndpointMemory("broker.invalid", "user", 1883, MqttTransport.Tcp);

        Assert.Null(MqttEndpointPlan.Reusable(Request(), incomplete));
        Assert.NotNull(MqttEndpointPlan.Reusable(Request(encryption: MqttEncryptionMode.Off), incomplete));
    }

    [Fact]
    public void NextEndpoint_StopsOnceTheBrokerItselfHasAnswered()
    {
        var attempts = new List<MqttEndpointAttempt>
        {
            new(new(1883, MqttTransport.Tcp, true), MqttProbeOutcome.AuthRejected),
        };

        Assert.Null(MqttEndpointPlan.NextEndpoint(Request(), null, attempts));
    }

    [Fact]
    public void NextEndpoint_CarriesOnPastATlsFailureOnOneEndpoint()
    {
        // A refused certificate on one port says nothing about the next port or the other transport.
        var attempts = new List<MqttEndpointAttempt>
        {
            new(new(1883, MqttTransport.Tcp, true), MqttProbeOutcome.TlsFailed),
        };

        var next = MqttEndpointPlan.NextEndpoint(Request(), null, attempts);

        Assert.NotNull(next);
        Assert.NotEqual(new MqttEndpointCandidate(1883, MqttTransport.Tcp, false), next);
    }

    [Fact]
    public void NextEndpoint_FallsBackToPlainOnlyWhenNothingWasListening()
    {
        var nothingThere = new List<MqttEndpointAttempt>
        {
            new(new(1883, MqttTransport.Tcp, true), MqttProbeOutcome.Unreachable),
        };

        Assert.Equal(new MqttEndpointCandidate(1883, MqttTransport.Tcp, false),
            MqttEndpointPlan.NextEndpoint(Request(port: 1883, transport: MqttTransportMode.Tcp), null, nothingThere));
    }

    [Fact]
    public void NextEndpoint_NeverRetriesInClearTextAfterATlsFailure()
    {
        var refusedTheCertificate = new List<MqttEndpointAttempt>
        {
            new(new(1883, MqttTransport.Tcp, true), MqttProbeOutcome.TlsFailed),
        };

        Assert.Null(MqttEndpointPlan.NextEndpoint(
            Request(port: 1883, transport: MqttTransportMode.Tcp), null, refusedTheCertificate));
    }

    [Theory]
    [InlineData(MqttProbeOutcome.TlsFailed)]
    [InlineData(MqttProbeOutcome.TimedOut)]
    [InlineData(MqttProbeOutcome.Failed)]
    public void DowngradeBlocked_BlocksThePlainRetryWhenSomethingWasThere(MqttProbeOutcome outcome)
    {
        var attempts = new List<MqttEndpointAttempt> { new(new(1883, MqttTransport.Tcp, true), outcome) };

        Assert.True(MqttEndpointPlan.DowngradeBlocked(attempts, new(1883, MqttTransport.Tcp, false)));
    }

    [Fact]
    public void DowngradeBlocked_IsAboutOneEndpointAndNotTheWholeSweep()
    {
        var attempts = new List<MqttEndpointAttempt>
        {
            new(new(8883, MqttTransport.Tcp, true), MqttProbeOutcome.TlsFailed),
        };

        Assert.False(MqttEndpointPlan.DowngradeBlocked(attempts, new(1883, MqttTransport.Tcp, false)));
        Assert.False(MqttEndpointPlan.DowngradeBlocked(attempts, new(8883, MqttTransport.WebSocket, false)));
    }

    [Fact]
    public void DowngradeBlocked_NeverBlocksAnEncryptedCandidate()
    {
        var attempts = new List<MqttEndpointAttempt>
        {
            new(new(1883, MqttTransport.Tcp, true), MqttProbeOutcome.TlsFailed),
        };

        Assert.False(MqttEndpointPlan.DowngradeBlocked(attempts, new(1883, MqttTransport.Tcp, true)));
    }

    [Fact]
    public void ShouldProbe_NeedsPublishingOnAndAHost()
    {
        Assert.True(MqttEndpointPlan.ShouldProbe(MqttProbeTrigger.TestConnection, true, "broker.invalid"));
        Assert.False(MqttEndpointPlan.ShouldProbe(MqttProbeTrigger.TestConnection, false, "broker.invalid"));
        Assert.False(MqttEndpointPlan.ShouldProbe(MqttProbeTrigger.TestConnection, true, "  "));
    }

    [Fact]
    public void DescribeProvenance_SaysManualOnlyWhenAllThreeArePinned()
    {
        Assert.StartsWith(MqttEndpointPlan.SetManually,
            MqttEndpointPlan.DescribeProvenance(
                Request(1883, MqttTransportMode.Tcp, MqttEncryptionMode.Off), null),
            StringComparison.Ordinal);

        Assert.StartsWith(MqttEndpointPlan.AutomaticallyDetected,
            MqttEndpointPlan.DescribeProvenance(Request(1883, MqttTransportMode.Tcp), null),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeProvenance_NamesAClearTextLinkNobodyChose()
    {
        var memory = new MqttEndpointMemory("broker.invalid", "user", 1883, MqttTransport.Tcp, false);

        Assert.Contains("not encrypted", MqttEndpointPlan.DescribeProvenance(Request(), memory));
    }

    [Fact]
    public void EncryptionInForce_IsUnknownUnderAutomaticWithNothingConnectedYet() =>
        Assert.Null(MqttEndpointPlan.EncryptionInForce(Request(), null));

    [Fact]
    public void EncryptionInForce_IsTrueOnAPinnedWebSocketPortWhoseSchemeIsFixed()
    {
        var request = Request(443, MqttTransportMode.WebSocket, MqttEncryptionMode.Off);

        Assert.True(MqttEndpointPlan.EncryptionInForce(request, null));
    }
}
