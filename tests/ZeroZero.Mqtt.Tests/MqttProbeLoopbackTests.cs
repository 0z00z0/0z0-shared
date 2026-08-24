using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>End-to-end verdicts for the connection check, against loopback only. The pure
/// classifiers are covered separately; what these add is that a run reaches each one. MQTTnet
/// returns a refused CONNACK as a result code rather than throwing, and if that ever flips, the
/// credential verdict degrades silently into a generic failure.</summary>
public class MqttProbeLoopbackTests
{
    // Pinned to TCP and to plain: these exercise the socket and CONNACK stages against a listener
    // that speaks neither WebSocket nor TLS, so leaving either on Automatic would only add attempts
    // against something nothing here serves.
    private static MqttProbeTarget Target(int port) =>
        new("127.0.0.1", port, "user", "placeholder", ClientId: "exampleapp_probe",
            Transport: MqttTransportMode.Tcp, Encryption: MqttEncryptionMode.Off);

    [Fact]
    public async Task NothingListening_IsUnreachableAndNotACredentialFailure()
    {
        var report = await MqttProbe.RunAsync(Target(FakeBroker.ClosedPort()), CancellationToken.None);

        Assert.Equal(MqttProbeOutcome.Unreachable, report.Outcome);
        Assert.Contains("Could not reach the broker", MqttStatusText.Describe(report));
    }

    [Fact]
    public async Task ABrokerRefusingTheCredentials_IsAnAuthRejection()
    {
        using var broker = new FakeBroker(MqttConnackCode.NotAuthorised);

        var report = await MqttProbe.RunAsync(Target(broker.Port), CancellationToken.None);

        Assert.Equal(MqttProbeOutcome.AuthRejected, report.Outcome);
        Assert.Contains("rejected these credentials", MqttStatusText.Describe(report));
    }

    [Fact]
    public async Task ABrokerAcceptingTheConnection_IsASuccessAndNamesTheEndpoint()
    {
        using var broker = new FakeBroker();

        var report = await MqttProbe.RunAsync(Target(broker.Port), CancellationToken.None);

        Assert.True(report.Succeeded);
        Assert.Equal(broker.Port, report.Candidate.Port);
        Assert.Equal(MqttTransport.Tcp, report.Transport);
    }

    /// <summary>Automatic encryption against a broker with no TLS on its port, which is the ordinary
    /// internal broker on 1883. The encrypted attempt is hung up on, and everything then rests on
    /// that hang-up being read as "nothing secure was on offer" rather than as a generic failure —
    /// read the other way, the clear-text candidate behind it is never tried and the broker is
    /// unreachable. Against a listener rather than a constructed exception, because the shape of the
    /// failure is exactly what was got wrong.</summary>
    [Fact]
    public async Task AutomaticEncryptionAgainstAPlainBroker_RetriesInClearTextAndConnects()
    {
        using var broker = new FakeBroker();
        var target = new MqttProbeTarget("127.0.0.1", broker.Port, "user", "placeholder",
            ClientId: "exampleapp_probe", Transport: MqttTransportMode.Tcp,
            Encryption: MqttEncryptionMode.Auto);

        var report = await MqttProbe.RunAsync(target, CancellationToken.None);

        Assert.Equal(2, report.Attempts.Count);
        Assert.True(report.Attempts[0].Candidate.Encrypted);
        Assert.Equal(MqttProbeOutcome.TlsUnsupported, report.Attempts[0].Outcome);
        Assert.False(report.Attempts[1].Candidate.Encrypted);
        Assert.True(report.Succeeded);
    }

    /// <summary>Auto must actually try the second transport, not report the first one's failure as
    /// the whole answer. Nothing serves WebSocket on loopback either, so both attempts fail — what is
    /// asserted is that both were made and both are named. The port is pinned so the sweep is exactly
    /// the two transports.</summary>
    [Fact]
    public async Task AutoOnAPinnedPort_WhenTcpIsClosed_AlsoTriesWebSocket()
    {
        var target = new MqttProbeTarget("127.0.0.1", FakeBroker.ClosedPort(), "user", "placeholder",
            ClientId: "exampleapp_probe", Transport: MqttTransportMode.Auto,
            Encryption: MqttEncryptionMode.Off);

        var report = await MqttProbe.RunAsync(target, CancellationToken.None);

        Assert.Equal(2, report.Attempts.Count);
        Assert.Equal(MqttTransport.Tcp, report.Attempts[0].Candidate.Transport);
        Assert.Equal(MqttTransport.WebSocket, report.Attempts[1].Candidate.Transport);

        string sentence = MqttStatusText.Describe(report);
        Assert.Contains("TCP", sentence);
        Assert.Contains("WebSocket", sentence);
    }

    /// <summary>What is remembered is where the sweep starts, not where it stops. A dead entry must
    /// not strand the connection: the live broker sits elsewhere, and the sweep behind the stale
    /// entry has to reach it.</summary>
    [Fact]
    public async Task AStaleMemory_CostsOneAttemptAndNotTheConnection()
    {
        int dead = FakeBroker.ClosedPort();
        var stale = new MqttEndpointMemory("127.0.0.1", "user", dead, MqttTransport.Tcp, false);

        // The port is left to the search: pinning one would filter the stale entry out before it
        // could be tried, which is a different behaviour from the one under test here.
        var target = new MqttProbeTarget("127.0.0.1", null, "user", "placeholder",
            ClientId: "exampleapp_probe", Transport: MqttTransportMode.Tcp,
            Encryption: MqttEncryptionMode.Off, Memory: stale);

        var report = await MqttProbe.RunAsync(target, CancellationToken.None);

        Assert.Equal(dead, report.Attempts[0].Candidate.Port);
        Assert.True(report.Attempts.Count > 1, "losing the remembered endpoint must not end the run");
    }

    [Fact]
    public async Task ARun_ReportsTheStageItIsOnAndEachVerdict()
    {
        using var broker = new FakeBroker();
        var seen = new List<string>();

        await MqttProbe.RunAsync(Target(broker.Port), CancellationToken.None,
            new Progress<MqttSearchProgress>(p => { lock (seen) seen.Add(MqttStatusText.Describe(p)); }));

        // Progress posts asynchronously, so settle before reading rather than racing the last report.
        await FakeBroker.WaitAsync(() => { lock (seen) return seen.Count >= 3; }, TimeSpan.FromSeconds(2));
        lock (seen)
        {
            Assert.Contains($"Trying TCP on port {broker.Port}…", seen);
            Assert.Contains($"TCP on port {broker.Port} connected.", seen);
        }
    }

    [Fact]
    public async Task ACancelledRun_StopsRatherThanFinishingTheSweep()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var report = await MqttProbe.RunAsync(Target(FakeBroker.ClosedPort()), cts.Token);

        Assert.True(report.Attempts.Count <= 1, "at most the one attempt already in flight");
    }

    [Fact]
    public async Task ARunWithNoHostHasNothingToTry()
    {
        var report = await MqttProbe.RunAsync(
            new MqttProbeTarget("  ", 1883, "", "", "exampleapp_probe"), CancellationToken.None);

        Assert.Empty(report.Attempts);
        Assert.Equal("No broker host set.", MqttStatusText.Describe(report));
    }
}
