using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>What the maintain loop does when the connect path fails rather than succeeds, and what a
/// resume from standby forces. None of it runs in ordinary use, so nothing about a working
/// application says whether it is right: each of these leaves a connection that reads as alive while
/// doing nothing, and the only symptom is on the receiver.</summary>
public class MqttConnectionRecoveryTests
{
    private const string Root = "exampleapp";
    private const string Device = "desk01";

    private static string Availability => MqttTopics.Availability(Root, Device);

    private static MqttConnectParameters Parameters(int port) => new()
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = port,
        TransportMode = MqttTransportMode.Tcp,
        EncryptionMode = MqttEncryptionMode.Off,
        DeviceId = Device,
    };

    private static MqttConnectionSetup Setup(IMqttConnectionListener? listener = null) => new()
    {
        TopicRoot = Root,
        Listener = listener,
    };

    /// <summary>A listener that fails the one call the connection makes on connect.</summary>
    private sealed class ThrowingListener : IMqttConnectionListener
    {
        public int Calls;

        public Task OnConnectedAsync(IMqttPublisher publisher, MqttDeviceIdentity identity, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("the layer above could not announce itself");
        }

        public Task OnRemovingAsync(IMqttPublisher publisher, MqttDeviceIdentity identity, CancellationToken ct) =>
            Task.CompletedTask;

        public Task OnIdentityRetiredAsync(IMqttPublisher publisher, MqttDeviceIdentity retired, CancellationToken ct) =>
            Task.CompletedTask;
    }

    /// <summary>A throw on the connect path leaves the socket up and the session half-built: no
    /// announcement, no availability and no command subscription. Left standing it is never retried,
    /// because a connected client takes neither branch of the next pass — a device that reads as alive
    /// and answers nothing until the process restarts.</summary>
    [Fact]
    public async Task AThrowOnTheConnectPathDropsTheSocketRatherThanLeavingAHalfBuiltSession()
    {
        using var broker = new FakeBroker();
        var listener = new ThrowingListener();

        using var connection = new MqttConnection(Setup(listener));
        await connection.ApplyAsync(Parameters(broker.Port));

        // A second CONNECT is what says the socket went: a session left standing reads as connected,
        // so the loop takes neither branch again and the broker never hears from it twice.
        Assert.True(await FakeBroker.WaitAsync(() => broker.Connects >= 2),
            "a half-built session was left standing");
        Assert.Null(broker.LastPayload(Availability));   // and it was never announced online
        Assert.True(Volatile.Read(ref listener.Calls) >= 2);
    }

    /// <summary>The backoff has to survive the very path that needs it. The loop drops the socket
    /// itself when the connect sequence throws, MQTTnet raises that as a disconnect with the "was
    /// connected" flag set, and a wake armed on it cancels the wait the failed round had just
    /// earned — every round, so the retry rate is whatever a loopback socket costs rather than the
    /// escalating delay. Measured at 111 CONNECTs in three seconds before the fix, against a design
    /// that intends at most one.</summary>
    /// <remarks>Counted at the broker rather than read off the connection: the state oscillates far
    /// faster than anything can sample it, so a flag or a status line says nothing. The window is
    /// wall-clock and the count is monotonic, so a slow machine only ever lowers it.</remarks>
    [Fact]
    public async Task AThrowingConnectSequenceRetriesOnTheBackoffRatherThanContinuously()
    {
        using var broker = new FakeBroker();
        var listener = new ThrowingListener();

        using var connection = new MqttConnection(Setup(listener));
        await connection.ApplyAsync(Parameters(broker.Port));
        Assert.True(await FakeBroker.WaitAsync(() => broker.Connects >= 1), "it never connected at all");

        int before = broker.Connects;
        await Task.Delay(TimeSpan.FromSeconds(3));
        int inTheWindow = broker.Connects - before;

        Assert.True(inTheWindow <= 2,
            $"{inTheWindow} CONNECTs in three seconds; the backoff intends at most one");

        // And it is a backoff, not a stall: the round after still comes.
        Assert.True(await FakeBroker.WaitAsync(() => broker.Connects > before, TimeSpan.FromSeconds(20)),
            "the loop stopped retrying altogether");
    }

    /// <summary>The status callback runs inside the connect sequence, so a host whose handler throws
    /// once — a disposed control, a marshalling error — would otherwise have its own exception read as
    /// a connect failure and its socket dropped, on every pass, for ever.</summary>
    [Fact]
    public async Task AStatusSubscriberThatThrowsDoesNotCostTheConnection()
    {
        using var broker = new FakeBroker();

        using var connection = new MqttConnection(Setup());
        connection.StateChanged += _ => throw new InvalidOperationException("the host's status line is gone");
        await connection.ApplyAsync(Parameters(broker.Port));

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Availability) == "online"));
        Assert.Equal(MqttConnectionState.Connected, connection.State);
        Assert.Equal(1, broker.Connects);   // not a connect/throw/disconnect cycle
    }

    /// <summary>Modern standby suspends the NIC, so the socket comes back half-dead while the client
    /// still reads as connected. Without the forced bounce the entities sit at the Last Will "offline"
    /// until the long re-poll or the keep-alive happens to notice — and, on a socket that never
    /// errors, indefinitely.</summary>
    [Fact]
    public async Task AResumeFromStandbyBouncesTheSocketAndAnnouncesOnlineAgain()
    {
        using var broker = new FakeBroker();
        using var connection = new MqttConnection(Setup());
        await connection.ApplyAsync(Parameters(broker.Port));
        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Availability) == "online"));

        connection.OnPowerResume();

        Assert.True(await FakeBroker.WaitAsync(() => broker.Connects >= 2), "the socket was never bounced");
        Assert.True(await FakeBroker.WaitAsync(() => connection.IsConnected));
    }

    /// <summary>A round where nothing answered anywhere is retrying, not failed. The two drive the
    /// same status line and send a user to different fields: refused means the credentials, retrying
    /// means the host or the network.</summary>
    [Fact]
    public async Task ARoundWhereNothingAnsweredIsRetryingRatherThanFailed()
    {
        var states = new List<MqttConnectionState>();
        using var connection = new MqttConnection(Setup());
        connection.StateChanged += s => { lock (states) states.Add(s); };

        await connection.ApplyAsync(Parameters(FakeBroker.ClosedPort()));

        Assert.True(await FakeBroker.WaitAsync(
            () => { lock (states) return states.Contains(MqttConnectionState.Retrying); }));
        lock (states) Assert.DoesNotContain(MqttConnectionState.Failed, states);
    }

    /// <summary>Removal over a link that is not there removes nothing, and says so. A panel that reads
    /// the answer as "done" otherwise tells a user the device is gone from the receiver while every
    /// retained topic it owns is still standing.</summary>
    [Fact]
    public async Task RemovingTheDeviceOverNoLinkRemovesNothingAndSaysSo()
    {
        using var connection = new MqttConnection(Setup());

        Assert.False(await connection.RemoveDeviceAsync());
        Assert.Equal(MqttConnectionState.Disabled, connection.State);
    }
}
