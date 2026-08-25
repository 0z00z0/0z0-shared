using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The connection against a real listener speaking MQTT. What these are about is the wire:
/// that a value lands on its own bare topic, that a refused acknowledgement is read as a failed
/// publish rather than a delivered one, and that an inbound command reaches an asynchronous handler
/// off the receive callback. A stubbed client would assert only that the module calls the methods it
/// calls.</summary>
public class MqttConnectionLoopbackTests
{
    private const string Root = "exampleapp";
    private const string Device = "desk01";

    private static string Topic(string key) => MqttTopics.Channel(Root, Device, key);

    private static string Availability => MqttTopics.Availability(Root, Device);

    private static MqttConnectParameters Parameters(FakeBroker broker) => new()
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = broker.Port,
        TransportMode = MqttTransportMode.Tcp,
        EncryptionMode = MqttEncryptionMode.Off,
        DeviceId = Device,
    };

    private static async Task<MqttConnection> ConnectAsync(FakeBroker broker, MqttConnectionSetup setup)
    {
        var connection = new MqttConnection(setup);
        await connection.ApplyAsync(Parameters(broker));
        Assert.True(await FakeBroker.WaitAsync(() => connection.IsConnected), "the connection never came up");
        return connection;
    }

    private static MqttConnectionSetup Setup(
        IEnumerable<MqttChannel>? channels = null,
        IEnumerable<MqttCommandTarget>? commands = null,
        IEnumerable<MqttSubscription>? subscriptions = null,
        Action<MqttCommandRefusal>? refused = null,
        Action<MqttEndpointMemory>? remember = null) => new()
        {
            TopicRoot = Root,
            Channels = [.. channels ?? []],
            CommandTargets = [.. commands ?? []],
            Subscriptions = [.. subscriptions ?? []],
            CommandRefused = refused,
            RememberEndpoint = remember,
        };

    /// <summary>A command entity that takes only the two payloads its kind has, as a real one does.
    /// What makes an empty payload a refusal rather than something quietly accepted.</summary>
    private static MqttCommandTarget Switch(string entityId, Action<string>? applied = null) =>
        new(entityId, payload => payload is "ON" or "OFF"
            ? MqttCommandVerdict.Accept(() => applied?.Invoke(payload))
            : MqttCommandVerdict.Malformed($"'{payload}' is not ON or OFF."));

    [Fact]
    public async Task AConnectAnnouncesItselfOnlineAndSubscribesToTheCommandSubtree()
    {
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup());

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Availability) == "online"));
        Assert.True(await FakeBroker.WaitAsync(
            () => broker.Subscriptions.Contains(MqttTopics.CommandFilter(Root, Device))));
        Assert.Equal(MqttConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task EachEntityGetsItsOwnBareTopicCarryingAPlainValue()
    {
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup([
            new("cpu_load", () => "42"),
            new("quiet_mode", () => "ON"),
        ]));

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Topic("quiet_mode")) is not null));
        Assert.Equal("42", broker.LastPayload(Topic("cpu_load")));
        Assert.Equal("ON", broker.LastPayload(Topic("quiet_mode")));
        Assert.All(broker.Published.Where(p => p.Topic.StartsWith(Root, StringComparison.Ordinal)),
            p => Assert.True(p.Retained));
    }

    [Fact]
    public async Task AnUnchangedValueIsNotSentAgain()
    {
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42")]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        connection.Publish("cpu_load", "42");
        await Task.Delay(300);

        Assert.Equal(1, broker.CountOn(Topic("cpu_load")));
    }

    /// <summary>A publish the broker declined must not take the dedupe slot with it, or the value
    /// that never arrived is indistinguishable from one that did and the topic stays wrong until it
    /// happens to change again.</summary>
    [Fact]
    public async Task ARefusedPublishIsRolledBackAndSentAgainOnTheNextPass()
    {
        using var broker = new FakeBroker
        {
            PubackFor = topic => topic.EndsWith("cpu_load", StringComparison.Ordinal)
                ? MqttPubackCode.NotAuthorised
                : MqttPubackCode.Success,
        };
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42")]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        connection.Publish("cpu_load", "42");

        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) >= 2),
            "a refused publish was recorded as sent");
    }

    [Fact]
    public async Task ARefusedPublishIsNotRecordedAsActivity()
    {
        using var broker = new FakeBroker { PubackFor = _ => MqttPubackCode.QuotaExceeded };
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42")]));

        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) >= 1));

        Assert.Null(connection.Activity.LastPublish);
    }

    [Fact]
    public async Task ADeliveredPublishIsRecordedAsActivity()
    {
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42")]));

        Assert.True(await FakeBroker.WaitAsync(() => connection.Activity.LastPublish is not null));
    }

    [Fact]
    public async Task AnInboundCommandReachesAnAsynchronousHandlerWithAToken()
    {
        using var broker = new FakeBroker();
        var ran = new TaskCompletionSource<string>();
        bool cancellable = false;
        using var connection = await ConnectAsync(broker, Setup(
            commands: [new("quiet_mode", payload => MqttCommandVerdict.Accept(async ct =>
            {
                cancellable = ct.CanBeCanceled;
                await Task.Yield();
                ran.TrySetResult(payload);
            }))]));

        await broker.SendAsync(MqttTopics.Command(Root, Device, "quiet_mode"), "ON");

        Assert.Equal("ON", await ran.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(cancellable);
        Assert.Equal("quiet_mode", connection.Activity.LastCommand?.EntityId);
    }

    [Fact]
    public async Task ARetainedCommandIsDroppedAndReportedRatherThanActedOn()
    {
        using var broker = new FakeBroker();
        var refusals = new List<MqttCommandRefusal>();
        bool ran = false;
        using var connection = await ConnectAsync(broker, Setup(
            commands: [new("quiet_mode", _ => MqttCommandVerdict.Accept(() => ran = true))],
            refused: r => { lock (refusals) refusals.Add(r); }));

        await broker.SendAsync(MqttTopics.Command(Root, Device, "quiet_mode"), "ON", retained: true);

        Assert.True(await FakeBroker.WaitAsync(() => { lock (refusals) return refusals.Count == 1; }));
        Assert.False(ran);
        Assert.Equal(MqttCommandOutcome.Retained, refusals[0].Outcome);
        Assert.Null(connection.Activity.LastCommand);
    }

    [Fact]
    public async Task ARefusalCarriesTheApplicationsOwnWordingToItsOwnSink()
    {
        using var broker = new FakeBroker();
        var refusals = new List<MqttCommandRefusal>();
        using var connection = await ConnectAsync(broker, Setup(
            commands: [new("power", _ => MqttCommandVerdict.Refuse("'Shutdown' is not available while it is off."))],
            refused: r => { lock (refusals) refusals.Add(r); }));

        await broker.SendAsync(MqttTopics.Command(Root, Device, "power"), "Shutdown");

        Assert.True(await FakeBroker.WaitAsync(() => { lock (refusals) return refusals.Count == 1; }));
        Assert.Equal("'Shutdown' is not available while it is off.", refusals[0].Detail);
        Assert.Equal("power", refusals[0].EntityId);
    }

    [Fact]
    public async Task ASubscriptionOutsideTheCommandTreeGetsItsOwnHandler()
    {
        using var broker = new FakeBroker();
        var arrived = new TaskCompletionSource<MqttInboundMessage>();
        using var connection = await ConnectAsync(broker, Setup(
            subscriptions: [new("homeassistant/status", (message, _) =>
            {
                arrived.TrySetResult(message);
                return Task.CompletedTask;
            })]));

        Assert.True(await FakeBroker.WaitAsync(() => broker.Subscriptions.Contains("homeassistant/status")));
        await broker.SendAsync("homeassistant/status", "online");

        var message = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("online", message.Payload);
    }

    /// <summary>No current reading empties the topic, so a consumer connecting later sees nothing
    /// rather than a value of unknown age — and it does so once, not on every pass.</summary>
    [Fact]
    public async Task AChannelWithNoCurrentReadingEmptiesItsTopicExactlyOnce()
    {
        using var broker = new FakeBroker();
        string? reading = "42";
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => reading)]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        reading = null;
        connection.RequestPublish("cpu_load");
        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Topic("cpu_load")) == ""));

        connection.RequestPublish("cpu_load");
        await Task.Delay(300);

        Assert.Equal(2, broker.CountOn(Topic("cpu_load")));
    }

    /// <summary>A channel whose producer has a first reading to wait for keeps what it last published
    /// rather than emptying its topic, and sends it again on a connect — so a receiver that came back
    /// with nothing retained has a value rather than a blank.</summary>
    [Fact]
    public async Task AChannelThatKeepsItsLastValueIsNotEmptiedAndIsSentAgainOnConnect()
    {
        using var broker = new FakeBroker();
        string? reading = "42";
        using var connection = await ConnectAsync(broker, Setup([
            new("cpu_load", () => reading, RepublishLastOnConnect: true),
        ]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        reading = null;
        connection.RequestPublish("cpu_load");
        await Task.Delay(300);

        Assert.Equal(1, broker.CountOn(Topic("cpu_load")));
        Assert.Equal("42", broker.LastPayload(Topic("cpu_load")));

        // A changed parameter set reconnects, which is the only way this channel is asked again while
        // its reading is still absent.
        await connection.ApplyAsync(Parameters(broker) with { DeviceName = "Workshop" });

        Assert.True(await FakeBroker.WaitAsync(
            () => broker.CountOn(Topic("cpu_load")) == 2, TimeSpan.FromSeconds(30)));
        Assert.Equal("42", broker.LastPayload(Topic("cpu_load")));
    }

    /// <summary>A reader that threw says nothing about the current value, so what stands, stands.
    /// Emptying the topic here would assert "no value" on the strength of a bug in the reader.</summary>
    [Fact]
    public async Task AThrowingReaderLeavesTheLastPublishedValueStanding()
    {
        using var broker = new FakeBroker();
        bool broken = false;
        using var connection = await ConnectAsync(broker, Setup([
            new("cpu_load", () => broken ? throw new InvalidOperationException("no reading") : "42"),
        ]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        broken = true;
        connection.RequestPublish("cpu_load");
        await Task.Delay(300);

        Assert.Equal(1, broker.CountOn(Topic("cpu_load")));
        Assert.Equal("42", broker.LastPayload(Topic("cpu_load")));
    }

    [Fact]
    public async Task PublishNow_SendsEveryChannelWhetherOrNotItMoved()
    {
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup([
            new("cpu_load", () => "42"),
            new("quiet_mode", () => "ON"),
        ]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("quiet_mode")) == 1));

        Assert.True(await connection.PublishNowAsync());

        Assert.Equal(2, broker.CountOn(Topic("cpu_load")));
        Assert.Equal(2, broker.CountOn(Topic("quiet_mode")));
    }

    /// <summary>A group toggle rebuilds the channel set, and the entities that left have to be
    /// evicted in one pass rather than one sequential retained publish each.</summary>
    [Fact]
    public async Task SetChannels_EmptiesTheTopicsOfTheEntitiesThatHaveGone()
    {
        using var broker = new FakeBroker();
        var withheld = Enumerable.Range(0, 12).Select(i => new MqttChannel($"metric_{i}", () => "1")).ToList();
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42"), .. withheld]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("metric_11")) == 1));

        await connection.SetChannelsAsync([new("cpu_load", () => "42")]);

        Assert.All(withheld, channel => Assert.Equal("", broker.LastPayload(Topic(channel.Key))));
        Assert.Equal(1, broker.CountOn(Topic("cpu_load")));
    }

    /// <summary>Idempotence is what makes "apply on every settings change" safe: a group toggle and a
    /// remembered endpoint both leave the projection identical and must not bounce the socket.</summary>
    /// <remarks>The changed apply comes first as a control, so a reconnect is known to be visible;
    /// then a round trip after the repeated apply proves the session that carried it is the same
    /// one, rather than a silence that might only mean the test was not looking long enough.</remarks>
    [Fact]
    public async Task ApplyingTheSameParametersAgainDoesNotBounceTheSocket()
    {
        using var broker = new FakeBroker();
        string reading = "42";
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => reading)]));
        Assert.Equal(1, broker.Connects);
        var moved = Parameters(broker) with { DeviceName = "Workshop" };

        await connection.ApplyAsync(moved);
        Assert.True(await FakeBroker.WaitAsync(() => broker.Connects == 2, TimeSpan.FromSeconds(30)),
            "a changed parameter set must reconnect, or the rest of this proves nothing");

        await connection.ApplyAsync(moved);
        reading = "43";
        connection.RequestPublish("cpu_load");

        Assert.True(await FakeBroker.WaitAsync(
            () => broker.LastPayload(Topic("cpu_load")) == "43", TimeSpan.FromSeconds(30)));
        Assert.Equal(2, broker.Connects);
    }

    [Fact]
    public async Task AConnectHandsTheEndpointToTheHostRatherThanToTheSettings()
    {
        using var broker = new FakeBroker();
        MqttEndpointMemory? remembered = null;
        using var connection = await ConnectAsync(broker, Setup(remember: m => remembered = m));

        Assert.True(await FakeBroker.WaitAsync(() => remembered is not null));
        Assert.Equal(broker.Port, remembered!.Port);
        Assert.Equal(MqttTransport.Tcp, remembered.Transport);
        Assert.False(remembered.Encrypted);
    }

    [Fact]
    public async Task ABrokerRefusingTheCredentialsEndsInFailedRatherThanRetrying()
    {
        using var broker = new FakeBroker(MqttConnackCode.NotAuthorised);
        using var connection = new MqttConnection(Setup());
        var states = new List<MqttConnectionState>();
        connection.StateChanged += s => { lock (states) states.Add(s); };

        await connection.ApplyAsync(Parameters(broker));

        Assert.True(await FakeBroker.WaitAsync(() => connection.State == MqttConnectionState.Failed));
        lock (states) Assert.Contains(MqttConnectionState.Failed, states);
        Assert.False(connection.IsConnected);
    }

    [Fact]
    public async Task SwitchingPublishingOffGoesOfflineAndLeavesEverythingStanding()
    {
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42")]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        await connection.ApplyAsync(Parameters(broker) with { Enabled = false });

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Availability) == "offline"));
        Assert.Equal("42", broker.LastPayload(Topic("cpu_load")));
        Assert.Equal(MqttConnectionState.Disabled, connection.State);
    }

    [Fact]
    public async Task AnIncompleteConfigurationStopsPublishingAndRemovesNothing()
    {
        // Reached by clearing one field. A user blanking the host to pause publishing must not take
        // the device off the receiver with it, so this path behaves exactly as the master switch does.
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42")]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        await connection.ApplyAsync(Parameters(broker) with { Host = "" });

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Availability) == "offline"));
        Assert.Equal("42", broker.LastPayload(Topic("cpu_load")));
    }

    [Fact]
    public async Task AChannelDroppedWhileDisconnectedIsEmptiedOnTheNextConnect()
    {
        // The replace has already forgotten the key by the time the link is checked, so a caller that
        // simply returns has lost it: nothing left knows the topic was ever published, and the value
        // stays retained on the broker for ever.
        using var broker = new FakeBroker();
        using var connection = new MqttConnection(
            Setup([new("cpu_load", () => "42"), new("gpu_load", () => "7")]));

        await connection.SetChannelsAsync([new("cpu_load", () => "42")]);
        await connection.ApplyAsync(Parameters(broker));

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Topic("gpu_load")) == ""));
    }

    [Fact]
    public async Task AChannelThatArrivesIsAskedForItsValueAtOnce()
    {
        // Its topic holds nothing — either it never did, or it was emptied when the key last went —
        // so an entity coming back after a group was re-ticked would otherwise read as unknown until
        // something else happened to signal it.
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42")]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        await connection.SetChannelsAsync(
            [new("cpu_load", () => "42"), new("gpu_load", () => "7")]);

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Topic("gpu_load")) == "7"));
    }

    [Fact]
    public async Task RemovingTheDeviceEmptiesEveryTopicItOwned()
    {
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42")]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        Assert.True(await connection.RemoveDeviceAsync());

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Availability) == ""));
        Assert.Equal("", broker.LastPayload(Topic("cpu_load")));
        Assert.Equal(MqttConnectionState.Disabled, connection.State);
    }

    [Fact]
    public async Task RemovingTheDeviceClearsACommandTopicSomethingLeftRetained()
    {
        // Only whoever put it there could otherwise clear it, and it would be redelivered the moment
        // the feature was switched on again.
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(
            broker,
            Setup([new("cpu_load", () => "42")],
                  [new("quiet_mode", _ => MqttCommandVerdict.Accept(() => { }))]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(Topic("cpu_load")) == 1));

        await connection.RemoveDeviceAsync();

        Assert.Equal("", broker.LastPayload(MqttTopics.Command(Root, Device, "quiet_mode")));
    }

    [Fact]
    public async Task ARetainedCommandIsEmptiedRatherThanRefusedForEver()
    {
        // A command is an event. Left retained it is redelivered and refused on every reconnect, and
        // nothing but the broker's owner could clear it.
        var refusals = new List<MqttCommandRefusal>();
        int applied = 0;
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(
            broker,
            Setup(commands: [new("quiet_mode", _ => MqttCommandVerdict.Accept(() => applied++))],
                  refused: r => { lock (refusals) refusals.Add(r); }));

        string topic = MqttTopics.Command(Root, Device, "quiet_mode");
        await broker.SendAsync(topic, "ON", retained: true);

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(topic) == ""));
        Assert.Equal(0, applied);
        lock (refusals) Assert.Contains(refusals, r => r.Outcome == MqttCommandOutcome.Retained);
    }

    [Fact]
    public async Task ANormalExitLeavesTheDeviceStandingAsOffline()
    {
        using var broker = new FakeBroker();
        var connection = await ConnectAsync(broker, Setup([new("cpu_load", () => "42")]));
        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Availability) == "online"));

        connection.Dispose();

        Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Availability) == "offline"));
        // The payload topics keep their values, so the device persists across a restart.
        Assert.Equal("42", broker.LastPayload(Topic("cpu_load")));
    }

    [Fact]
    public async Task AutomaticEncryptionAgainstAPlainBroker_ConnectsInClearText()
    {
        // The publisher, not only the connection check: what the defect cost was the live link to an
        // ordinary internal broker, so the live link is what has to reach one. The broker hangs up on
        // the encrypted attempt, and the plain candidate behind it is only tried if that hang-up is
        // read as "nothing secure was on offer".
        using var broker = new FakeBroker();
        var parameters = Parameters(broker) with { EncryptionMode = MqttEncryptionMode.Auto };
        MqttEndpointMemory? remembered = null;

        using var connection = new MqttConnection(Setup(remember: m => remembered = m));
        await connection.ApplyAsync(parameters);

        Assert.True(await FakeBroker.WaitAsync(() => connection.IsConnected), "the connection never came up");
        Assert.Equal(false, remembered?.Encrypted);
        Assert.Equal(broker.Port, remembered?.Port);
    }

    /// <summary>The socket stage runs for every candidate, not only for an encrypted one under
    /// Automatic. A far end that drops the packet rather than refusing it says nothing and spends the
    /// whole connect budget doing so, and no candidate may be allowed to spend that where there is
    /// another one to move on to.</summary>
    [Fact]
    public async Task EveryCandidateOpensASocketBeforeAnyMqttIsSpoken()
    {
        // Plain TCP, pinned: the shape the check used to skip entirely.
        using var broker = new FakeBroker();
        using var connection = await ConnectAsync(broker, Setup());

        Assert.Equal(1, broker.Connects);
        Assert.True(broker.Accepts > broker.Connects,
            "the candidate spoke MQTT without a socket having been opened first");
    }

    /// <summary>A removal empties the command topics while this connection is still subscribed to its
    /// own command wildcard, so without the unsubscribe the broker hands every clear straight back and
    /// the router refuses each one — a burst of the module's own messages in the host's log at exactly
    /// the moment something notable is happening.</summary>
    [Fact]
    public async Task RemovingTheDeviceReportsNoCommandOfItsOwn()
    {
        using var broker = new FakeBroker();
        var refusals = new List<MqttCommandRefusal>();
        var commands = Enumerable.Range(0, 8).Select(i => Switch($"quiet_mode_{i}")).ToList();

        using var connection = await ConnectAsync(
            broker,
            Setup([new("cpu_load", () => "42")], commands,
                  refused: r => { lock (refusals) refusals.Add(r); }));
        Assert.True(await FakeBroker.WaitAsync(
            () => broker.Subscriptions.Contains(MqttTopics.CommandFilter(Root, Device))));

        Assert.True(await connection.RemoveDeviceAsync());

        // The clears landed, so whatever they would have been handed back has had its chance.
        Assert.Equal("", broker.LastPayload(MqttTopics.Command(Root, Device, "quiet_mode_7")));
        await Task.Delay(300);
        lock (refusals) Assert.Empty(refusals);
    }

    /// <summary>Outside a removal a zero-length payload on a command topic is a message like any
    /// other, and reporting it is how a retained command being cleared is accounted for. The removal's
    /// silence must not become a general one.</summary>
    [Fact]
    public async Task AZeroLengthCommandOutsideARemovalIsStillReported()
    {
        using var broker = new FakeBroker();
        var refusals = new List<MqttCommandRefusal>();
        using var connection = await ConnectAsync(
            broker,
            Setup(commands: [Switch("quiet_mode")], refused: r => { lock (refusals) refusals.Add(r); }));

        await broker.SendAsync(MqttTopics.Command(Root, Device, "quiet_mode"), "");

        Assert.True(await FakeBroker.WaitAsync(() => { lock (refusals) return refusals.Count == 1; }));
        lock (refusals) Assert.Equal(MqttCommandOutcome.Malformed, refusals[0].Outcome);
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        // IDisposable requires it, and a host that tears down explicitly and then disposes on exit
        // does it. The second call used to cancel an already-disposed cancellation source.
        using var broker = new FakeBroker();
        var connection = new MqttConnection(Setup());
        connection.Apply(Parameters(broker));

        connection.Dispose();
        connection.Dispose();
    }

    [Fact]
    public void TeardownIsBoundedRatherThanOpenEnded()
    {
        // Reached from a host's Exit command on the UI thread, with a QoS 1 publish possibly in
        // flight into a half-dead socket.
        using var broker = new FakeBroker();
        var connection = new MqttConnection(Setup());
        connection.Apply(Parameters(broker));

        var started = DateTimeOffset.UtcNow;
        connection.Dispose();

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5),
            "teardown must be bounded, not open-ended");
    }
}
