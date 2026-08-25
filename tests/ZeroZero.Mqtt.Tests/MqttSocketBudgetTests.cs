using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>What the stage that opens a socket may spend, and what it makes of a far end that says
/// nothing at all. The two failures are told apart here: a refusal comes back at once and costs
/// nothing, while a dropped packet says nothing and would otherwise spend a whole connect budget per
/// candidate before the one that works is reached.</summary>
public class MqttSocketBudgetTests
{
    [Fact]
    public void SilentTimeout_IsTheShortestOfTheBudgets()
    {
        Assert.True(MqttProbe.SilentTimeout < MqttProbe.SweepTimeout);
        Assert.True(MqttProbe.SweepTimeout < MqttProbe.Timeout);
    }

    [Fact]
    public void SocketBudget_IsShortWhereThereIsAnotherCandidateToMoveOnTo() =>
        Assert.Equal(MqttProbe.SilentTimeout, MqttProbe.SocketBudget(candidates: 14, escalated: false));

    /// <summary>A pinned port, transport and encryption sweep exactly one candidate. There is nothing
    /// to move on to, so cutting it short would turn a slow broker into no broker rather than into a
    /// later candidate.</summary>
    [Fact]
    public void SocketBudget_IsTheFullOneForASweepOfOneCandidate()
    {
        Assert.Equal(MqttProbe.Timeout, MqttProbe.SocketBudget(candidates: 1, escalated: false));
        Assert.Equal(MqttProbe.Timeout, MqttProbe.SocketBudget(candidates: 0, escalated: false));
    }

    /// <summary>The guarantee across rounds: a broker on a link slower than the short budget connects
    /// on the round after rather than never.</summary>
    [Fact]
    public void SocketBudget_IsTheFullOneOnceARoundHasEscalated() =>
        Assert.Equal(MqttProbe.Timeout, MqttProbe.SocketBudget(candidates: 14, escalated: true));

    [Fact]
    public void ShouldEscalate_AfterARoundInWhichNothingAnswered()
    {
        var silent = new List<MqttEndpointAttempt>
        {
            new(new(1883, MqttTransport.Tcp, true), MqttProbeOutcome.Unreachable),
            new(new(1883, MqttTransport.Tcp, false), MqttProbeOutcome.Unreachable),
        };

        Assert.True(MqttConnection.ShouldEscalateSocketBudget(silent));
    }

    [Fact]
    public void ShouldEscalate_NotWhenTheBrokerItselfAnswered()
    {
        // It was reached, and what it said back is not a question of timing.
        var answered = new List<MqttEndpointAttempt>
        {
            new(new(1883, MqttTransport.Tcp, true), MqttProbeOutcome.Unreachable),
            new(new(1883, MqttTransport.Tcp, false), MqttProbeOutcome.AuthRejected),
        };

        Assert.False(MqttConnection.ShouldEscalateSocketBudget(answered));
    }

    [Fact]
    public void ShouldEscalate_NotForARoundThatTriedNothing() =>
        Assert.False(MqttConnection.ShouldEscalateSocketBudget([]));

    /// <summary>A far end that drops the packet leaves the socket stage with nothing whatsoever to go
    /// on, and the budget running out is the verdict.</summary>
    /// <remarks>Asserted against an expired budget rather than against a filtered address: whether a
    /// given machine black-holes an unrouted address or refuses it at once depends on its routing
    /// table, and the decision under test does not. An expired budget is the state a dropped packet
    /// leaves the stage in either way.</remarks>
    [Fact]
    public async Task ASocketThatNeverOpens_IsUnreachableAndNotTimedOut()
    {
        using var broker = new FakeBroker();
        using var expired = new CancellationTokenSource();
        await expired.CancelAsync();

        var result = await MqttProbe.ProbeTcpAsync(
            "127.0.0.1", broker.Port, expired.Token, CancellationToken.None);

        // TimedOut is "something is there and it did not answer", which is exactly what is not known.
        Assert.Equal(MqttProbeOutcome.Unreachable, result?.Outcome);
    }

    /// <summary>The caller giving up is not the far end saying nothing, and reporting it as an
    /// unreachable endpoint would put a verdict on the broker for something the user did.</summary>
    [Fact]
    public async Task ACancelledCaller_IsNotReportedAsAnUnreachableEndpoint()
    {
        using var broker = new FakeBroker();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await MqttProbe.ProbeTcpAsync(
            "127.0.0.1", broker.Port, cancelled.Token, cancelled.Token);

        Assert.Equal(MqttProbeOutcome.Failed, result?.Outcome);
    }

    /// <summary>The security-relevant edge of the short budget. A candidate whose socket never opened
    /// offered no encryption and carried no credential, so the plain retry behind it stays open — but
    /// one that presented a certificate and then failed still blocks it, and no shortening of the
    /// socket stage may turn a handshake failure into a clear-text retry.</summary>
    [Fact]
    public async Task ASilentCandidate_LeavesThePlainRetryOpenAndACertificateFailureStillBlocksIt()
    {
        using var broker = new FakeBroker();
        using var expired = new CancellationTokenSource();
        await expired.CancelAsync();

        var silent = await MqttProbe.ProbeTcpAsync(
            "127.0.0.1", broker.Port, expired.Token, CancellationToken.None);
        var plain = new MqttEndpointCandidate(1883, MqttTransport.Tcp, false);

        Assert.True(MqttEndpointPlan.DowngradeSafe(silent!.Value.Outcome));
        Assert.False(MqttEndpointPlan.DowngradeBlocked(
            [new(new(1883, MqttTransport.Tcp, true), silent.Value.Outcome)], plain));

        Assert.True(MqttEndpointPlan.DowngradeBlocked(
            [new(new(1883, MqttTransport.Tcp, true), MqttProbeOutcome.TlsUntrusted)], plain));
    }
}
