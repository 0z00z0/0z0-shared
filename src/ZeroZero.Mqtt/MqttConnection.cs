using MQTTnet;
using ZeroZero.Primitives;

namespace ZeroZero.Mqtt;

/// <summary>The broker connection: the client, the maintain loop, backoff, the candidate sweep, the
/// availability and will, the command channel and its worker, and the retained channels with their
/// dedupe. Never logs the broker password or any payload.</summary>
/// <remarks>Knows nothing of discovery, entities or components. Whatever has to happen on connect
/// above this layer arrives as an <see cref="IMqttConnectionListener"/>.</remarks>
public sealed class MqttConnection : IMqttPublisher, IDisposable
{
    private readonly MqttConnectionSetup _setup;
    private readonly ILogSink _log;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly MqttChannelSet _channels;
    private readonly MqttCommandRouter _router;
    private volatile IReadOnlyList<MqttSubscription> _subscriptions;
    private readonly MqttInbound _inbound;

    private volatile bool _enabled;
    private MqttConnectParameters? _parameters;
    private MqttEndpointMemory? _memory;
    private MqttDeviceIdentity _identity = new("", MqttSettings.DefaultDiscoveryPrefix, "");
    private string _availabilityTopic = "";
    private int _state = (int)MqttConnectionState.Disabled;

    private readonly MqttOwnedTopics _owned;
    private readonly MqttChannelPublisher _channelPublisher;

    // Honoured on the maintain-loop thread, so the forced socket drop cannot race its own connect.
    private volatile bool _reconnectRequested;

    private readonly MqttOutageLog _outage;

    // Whether the socket stage takes the full budget because a whole round went by with nothing
    // answering anywhere. Written under _gate on an apply and on the maintain-loop thread on a
    // connect, read there once per round.
    private volatile bool _socketBudgetEscalated;

    private readonly MqttLoopSignals _signals = new();

    private CancellationTokenSource? _cts;

    // A second Dispose must be harmless, and a host that tears down explicitly and then disposes on
    // exit makes it happen. Without the latch the second call cancels an already-disposed source.
    private int _disposed;

    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(60);

    // Drop detection is event-driven (the client's disconnect event wakes the loop), so this is only
    // a stability re-check and can be long — a device on limited power is not woken every few
    // seconds for nothing.
    private static readonly TimeSpan ConnectedPoll = TimeSpan.FromSeconds(60);

    internal static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(3);

    // One candidate's handshake, after its socket stage (MqttProbe.SocketBudget). A sweeping check
    // allows MqttProbe.SweepTimeout in all, so a slower handshake connects here but fails the check.
    private static readonly TimeSpan ConnectTimeout = MqttProbe.Timeout;

    private const double MaxBackoffSeconds = 60;

    // A session shorter than this is a flap, so its backoff keeps escalating instead of resetting.
    private static readonly TimeSpan StableConnection = TimeSpan.FromSeconds(30);

    // How many QoS 1 publishes are allowed in flight at once. A group toggle or an identity eviction
    // moves every topic in one pass, and one round trip per topic is seconds on a remote broker.
    // Sixteen stays inside the in-flight window brokers commonly cap at twenty.
    private const int MaxInFlightPublishes = 16;

    /// <summary>The debounce a channel wants when its value is signalled by something that has just
    /// written to the thing being read. Lets the in-progress write land before the read, so no
    /// interim state is published, and collapses a burst of signals into one read.</summary>
    public static readonly TimeSpan ReflectDebounce = TimeSpan.FromMilliseconds(250);

    public MqttConnection(MqttConnectionSetup setup)
    {
        _setup = setup;
        _log = setup.Log;
        _outage = new MqttOutageLog(_log);
        _channels = new MqttChannelSet(setup.Channels);
        _router = new MqttCommandRouter(setup.CommandTargets);
        _subscriptions = setup.Subscriptions;
        _inbound = new MqttInbound(
            setup, _router, Activity, () => _identity.DeviceId, () => _subscriptions,
            (topic, ct) => PublishAsync(topic, "", retain: true, ct: ct));
        _owned = new MqttOwnedTopics(setup, this, () => _client.IsConnected, () => _identity.DeviceId);
        _channelPublisher = new MqttChannelPublisher(
            setup, _channels, this, () => _enabled, () => IsConnected, () => _client.IsConnected,
            () => _identity.DeviceId);

        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += _inbound.OnMessageReceivedAsync;
        _client.DisconnectedAsync += OnClientDisconnectedAsync;
    }

    /// <summary>When something last reached the broker, and what the broker last asked for. Owned by
    /// this connection: a consumer holding a reference detects a rebuilt connection by identity.</summary>
    public MqttActivity Activity { get; } = new();

    /// <summary>The device id in force — the client id, the topic segment and the <c>unique_id</c>
    /// stem. Empty until the first apply.</summary>
    public string DeviceId => _identity.DeviceId;

    /// <summary>The identity in force.</summary>
    public MqttDeviceIdentity Identity => _identity;

    /// <summary>What the connection is doing. Cheap and safe to read from any thread.</summary>
    public MqttConnectionState State => (MqttConnectionState)Volatile.Read(ref _state);

    /// <summary>Raised on every state change, on whichever background thread made it.</summary>
    public event Action<MqttConnectionState>? StateChanged;

    /// <summary>Whether there is a live link to publish onto: the feature running and the client
    /// connected. What a "publish now" can act on, and what a settings panel gates its button by.</summary>
    public bool IsConnected => _enabled && _client.IsConnected;

    /// <summary>Where the broker last answered, as the sweep will lead with it.</summary>
    public MqttEndpointMemory? RememberedEndpoint => Volatile.Read(ref _memory);

    /// <summary>Reconciles to the parameters' desired state; safe to call repeatedly, and does
    /// nothing when handed parameters it already has.</summary>
    /// <remarks>Idempotence is what makes "apply on every settings change" safe. A group toggle and a
    /// remembered endpoint both leave the projection identical, so neither bounces the socket.</remarks>
    public void Apply(MqttConnectParameters parameters) => _ = ApplyAsync(parameters);

    /// <summary>The awaitable form, for a caller that needs the reconcile to have happened.</summary>
    public async Task ApplyAsync(MqttConnectParameters parameters)
    {
        // A torn-down connection reconciles to nothing. The client is already disposed, so a maintain
        // loop started from here would fail every round for the rest of the process.
        if (Volatile.Read(ref _disposed) != 0) return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A teardown that took the gate first has already stopped everything this would start.
            if (Volatile.Read(ref _disposed) != 0) return;

            if (!parameters.ShouldRun)
            {
                // Switched off, or a configuration that has stopped being complete — a blanked host is
                // reached by editing one field. Both stop publishing and leave everything in place:
                // the device stays, its values stay, and it reads as offline. Removing it is
                // RemoveDeviceAsync, which nothing here can arrive at by accident.
                _parameters = parameters;
                await StopInternalAsync(remove: false).ConfigureAwait(false);
                SetState(MqttConnectionState.Disabled);
                return;
            }

            bool wasRunning = _enabled;
            if (wasRunning && _parameters is { } previous && previous == parameters) return;

            _parameters = parameters;
            _memory = _setup.RecallEndpoint?.Invoke() ?? _memory;

            // These are different values against a possibly different network, so what an earlier one
            // had to be given the full socket budget for says nothing here.
            _socketBudgetEscalated = false;

            string machine = Environment.MachineName;
            var previousIdentity = _identity;
            string deviceId = MqttIdentity.Effective(parameters.DeviceId, _setup.TopicRoot, machine);
            _identity = new(
                deviceId,
                MqttSettings.EffectiveDiscoveryPrefix(parameters.DiscoveryPrefix),
                string.IsNullOrWhiteSpace(parameters.DeviceName)
                    ? _setup.DefaultDeviceName(machine)
                    : parameters.DeviceName.Trim());
            _availabilityTopic = MqttTopics.Availability(_setup.TopicRoot, deviceId);

            // The id is the device identity end to end, so a change orphans every retained topic the
            // old id owned. Record it under the prefix it was published with, which may have changed
            // in this same call.
            if (previousIdentity.DeviceId.Length > 0 && previousIdentity.DeviceId != deviceId)
            {
                _log.Info($"MQTT: device id '{previousIdentity.DeviceId}' → '{deviceId}'; evicting the old device.");
                _owned.HoldRetired(new(previousIdentity, _channels.Keys, _router.EntityIds));
            }
            await _owned.ClearRetiredIdentityAsync(CancellationToken.None).ConfigureAwait(false);

            _enabled = true;
            if (!wasRunning)
            {
                // A cancelled loop may still be unwinding; abandon it and start a fresh one — it exits
                // on its own token. Completion lags cancellation, so it cannot gate the restart.
                // Capture the token in a local: a later apply could swap the field before the lambda
                // runs.
                var cts = new CancellationTokenSource();
                _cts?.Dispose();
                _cts = cts;
                _ = Task.Run(() => MaintainConnectionAsync(cts.Token));
            }
            else
            {
                // Options changed while running. The loop drops the socket and reconnects with them at
                // once: a backoff earned under the old values says nothing about the new ones, and with
                // the link already down a drop alone would leave the loop waiting that backoff out.
                _reconnectRequested = true;
                Wake();
            }
        }
        // Apply discards this task, so an unhandled throw would silently disable the feature.
        catch (Exception ex) { _log.Error($"MqttConnection.Apply: {MqttProbe.Describe(ex)}", null); }
        finally { _gate.Release(); }
    }

    /// <summary>One connect round's verdict, and every candidate it got as far as trying.</summary>
    private readonly record struct ConnectRound(
        bool Connected, IReadOnlyList<MqttEndpointAttempt> Attempts);

    /// <summary>Connects over the first candidate <see cref="MqttEndpointPlan"/> offers that works.</summary>
    /// <remarks>The remembered endpoint leads the sweep, so the usual reconnect is one attempt. When
    /// it has stopped working — the machine moved, or the broker was republished elsewhere — the
    /// sweep behind it finds the new one and the memory is rewritten, which is why a stale entry
    /// costs an attempt rather than the feature.</remarks>
    private async Task<ConnectRound> ConnectUsingPlanAsync(
        MqttConnectParameters parameters, CancellationToken ct)
    {
        var request = parameters.Request;
        var attempts = new List<MqttEndpointAttempt>();

        // Fixed for the round, so every candidate in it is judged by the same rule, and the sweep's
        // own length is what says whether there is anywhere else to move on to.
        var socketBudget = MqttProbe.SocketBudget(
            MqttEndpointPlan.Sweep(request, RememberedEndpoint).Count, _socketBudgetEscalated);

        while (MqttEndpointPlan.NextEndpoint(request, RememberedEndpoint, attempts) is { } candidate)
        {
            var address = MqttEndpoint.Resolve(parameters.Host, candidate);
            MqttProbeResult result;

            var closed = await MqttProbe
                .SocketStageAsync(address, socketBudget, ct, ct).ConfigureAwait(false);
            // The loop's own cancellation ends the round instead of being recorded against a candidate.
            ct.ThrowIfCancellationRequested();
            if (closed is { } verdict)
            {
                attempts.Add(new(candidate, verdict));
                continue;
            }

            // Whether the far end offered encryption at all, which is what decides if the plain
            // candidate behind this one may be tried.
            var witness = address.Encrypted ? new MqttHandshakeWitness() : null;

            // MQTTnet applies its own connect timeout only to a token that cannot be cancelled, so the
            // handshake carries its budget in the token it is given.
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshake.CancelAfter(ConnectTimeout);
            try
            {
                // MQTTnet hands a refused CONNACK back as a result code rather than throwing, so the
                // code has to be read — otherwise a rejection looks like a live connection until the
                // first publish fails.
                var connack = await _client
                    .ConnectAsync(BuildOptions(parameters, address, witness), handshake.Token)
                    .ConfigureAwait(false);
                result = MqttProbe.ClassifyConnack(
                    MqttClientWiring.ConnackCode(connack), connack?.ReasonString);
            }
            // Only the loop's own cancellation ends the loop. An expired budget, or a cancellation
            // MQTTnet raises itself, is a failed attempt like any other.
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                result = MqttProbe.ClassifyConnectException(ex, ct, witness?.Verdict);
            }

            if (result.Outcome == MqttProbeOutcome.Success)
            {
                // A link that works owes nothing to whatever an earlier round had to be given longer
                // for, and the endpoint just remembered leads the next sweep anyway.
                _socketBudgetEscalated = false;
                RememberEndpoint(request, candidate);
                attempts.Add(new(candidate, result));
                return new(true, attempts);
            }

            // A half-open session from a refused CONNACK would make the next attempt look connected.
            await DropOwnSessionAsync().ConfigureAwait(false);
            attempts.Add(new(candidate, result));
        }

        // Nothing anywhere answered, so the short socket budget is a candidate for why: the next round
        // opens its sockets under the full one.
        if (ShouldEscalateSocketBudget(attempts)) _socketBudgetEscalated = true;

        _outage.ReportFailedRound(attempts);
        return new(false, attempts);
    }

    /// <summary>Ends the current session, if there is one, on this connection's own account, so the
    /// disconnect it raises is not reported as a lost link. Never throws: the socket is going away
    /// either way, and every caller reconnects next.</summary>
    private async Task DropOwnSessionAsync()
    {
        try
        {
            if (!_client.IsConnected) return;
            _outage.OwnDrop();
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
        catch { /* the socket is going away either way */ }
    }

    /// <summary>Whether the next round's socket stage should take the full budget: this one tried
    /// something and nothing answered at all. Pure.</summary>
    /// <remarks>A round the broker answered in was reached, and what it said back is not a question of
    /// timing, so escalating there would buy nothing. A round nothing answered in is the one case the
    /// short budget could have caused, and escalating is what keeps a broker on a link slower than
    /// <see cref="MqttProbe.SilentTimeout"/> reachable at all rather than never: it connects on the
    /// round after, and the endpoint it connected on then leads the sweep.</remarks>
    public static bool ShouldEscalateSocketBudget(IReadOnlyList<MqttEndpointAttempt> attempts) =>
        attempts.Count > 0 && !attempts.Any(a => MqttEndpointPlan.Answered(a.Outcome));

    private MqttClientOptions BuildOptions(
        MqttConnectParameters parameters, MqttEndpointAddress address, MqttHandshakeWitness? witness)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithEndpoint(address, parameters.CertificateTrust, witness)
            .WithClientId(_identity.DeviceId)
            .WithCleanSession()
            // MQTTnet pings within this period on an idle link, so the broker will not drop a quiet
            // connection and a silently dead one surfaces rather than lingering "connected".
            .WithKeepAlivePeriod(KeepAlive)
            // Bounds what MQTTnet sends with no cancellable token, such as the disconnect, and the
            // socket's send timeout. It does not bound the connect, which is given a cancellable
            // token and carries its own budget in it.
            .WithTimeout(ConnectTimeout)
            .WithWillTopic(_availabilityTopic)
            .WithWillPayload(_setup.OfflinePayload)
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttClientWiring.Qos(MqttQos.AtLeastOnce));

        if (!string.IsNullOrEmpty(parameters.Username))
            builder = builder.WithCredentials(parameters.Username, parameters.Password());

        return builder.Build();
    }

    /// <summary>Hands the host where the broker answered. State, not a setting: the user's choices
    /// are left exactly as they made them, and the sweep is what reads this back. The username
    /// belongs to the entry because a broker commonly fronts a separate listener per account; the
    /// password never does, and nothing here may hold one.</summary>
    private void RememberEndpoint(MqttEndpointRequest request, MqttEndpointCandidate candidate)
    {
        var found = new MqttEndpointMemory(
            (request.Host ?? "").Trim(), (request.Username ?? "").Trim(),
            candidate.Port, candidate.Transport, candidate.Encrypted);
        if (found == RememberedEndpoint) return;

        Volatile.Write(ref _memory, found);
        _setup.RememberEndpoint?.Invoke(found);
    }

    private async Task MaintainConnectionAsync(CancellationToken ct)
    {
        var backoff = new MqttReconnectBackoff();

        // Publishing switched back on starts a fresh account, so its first failure is reported again.
        _outage.Forget();

        while (!ct.IsCancellationRequested && _enabled)
        {
            // A resume from standby or a settings change. Modern standby suspends the NIC, so after a
            // resume the socket is often half-dead while the client still reads as connected. Neither
            // is a flap, so the backoff resets.
            if (_reconnectRequested)
            {
                _reconnectRequested = false;
                await DropOwnSessionAsync().ConfigureAwait(false);
                backoff.Resume();
            }

            try
            {
                if (!_client.IsConnected && _parameters is { } parameters)
                {
                    // A session that died young is a flap; wait out the escalating backoff before
                    // retrying. A drop of a session that lasted, or a first attempt, reconnects at
                    // once.
                    if (backoff.BeforeConnect(DateTimeOffset.UtcNow) is { } flapWait)
                    {
                        SetState(MqttConnectionState.Retrying);
                        if (!await DelayOrWake(flapWait, ct).ConfigureAwait(false)) break;
                    }

                    SetState(MqttEndpointPlan.Sweep(parameters.Request, RememberedEndpoint).Count > 1
                        ? MqttConnectionState.Searching
                        : MqttConnectionState.Connecting);

                    // Only a drop of the session this attempt is about to start may shorten its poll.
                    _signals.ArmDrop();
                    var round = await ConnectUsingPlanAsync(parameters, ct).ConfigureAwait(false);
                    if (round.Connected)
                    {
                        await OnConnectedAsync(ct).ConfigureAwait(false);
                        backoff.Connected(DateTimeOffset.UtcNow);
                        SetState(MqttConnectionState.Connected);
                    }
                    else
                    {
                        // Every candidate failed; wait longer before the next round. A broker that
                        // answered and refused says waiting will not help.
                        backoff.Failed();
                        SetState(round.Attempts.Any(a => MqttEndpointPlan.Answered(a.Outcome))
                            ? MqttConnectionState.Failed
                            : MqttConnectionState.Retrying);
                    }
                }
                else if (_client.IsConnected)
                {
                    // Proven stable, so the next genuine drop reconnects fast.
                    backoff.SettleIfStable(DateTimeOffset.UtcNow);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // A sequence that failed because the link dropped under it was reported as the loss.
                if (_client.IsConnected)
                    _log.Error($"MqttConnection.Connect: the connect sequence failed — {MqttProbe.Describe(ex)}", null);
                // The connect sequence can throw with the socket up, leaving neither branch reachable
                // next pass — a healthy-looking connection that never gets its announcement,
                // availability or subscription. Drop it so the next pass retries.
                await DropOwnSessionAsync().ConfigureAwait(false);
                backoff.Failed();
                SetState(MqttConnectionState.Retrying);
            }

            // Long re-poll while healthy, backoff while failing. A resume cuts either short; a drop
            // cuts only the healthy poll short, because the failing wait is the backoff itself and
            // the drop that ends a failed round is commonly this loop's own.
            bool healthy = _client.IsConnected;
            if (!await DelayOrWake(healthy ? ConnectedPoll : backoff.Delay, ct, wakeOnDrop: healthy)
                .ConfigureAwait(false))
                break;
        }
    }

    /// <summary>Exponential backoff step, capped.</summary>
    public static TimeSpan NextBackoff(TimeSpan current) =>
        TimeSpan.FromSeconds(Math.Min(current.TotalSeconds * 2, MaxBackoffSeconds));

    /// <summary>Whether a disconnect event should wake the loop for an early reconnect. MQTTnet also
    /// raises it with the "was connected" flag clear when the connect itself fails, and waking on
    /// that short-circuits the backoff into near-continuous reconnect hammering.</summary>
    public static bool ShouldWakeOnDisconnect(bool enabled, bool clientWasConnected) =>
        enabled && clientWasConnected;

    /// <summary>Whether a session that lived <paramref name="lifetime"/> counts as stable, not a
    /// flap.</summary>
    public static bool IsStableConnection(TimeSpan lifetime) => lifetime >= StableConnection;

    /// <inheritdoc cref="MqttLoopSignals.DelayOrWake"/>
    internal Task<bool> DelayOrWake(TimeSpan delay, CancellationToken ct, bool wakeOnDrop = false) =>
        _signals.DelayOrWake(delay, ct, wakeOnDrop);

    internal void Wake() => _signals.Wake();

    /// <summary>Notes that the link went down, so the loop's long poll of a live session ends at once
    /// and "online" is republished, shrinking the window where a consumer shows the Last Will
    /// "offline" while the machine is alive. A backoff wait is left to run: see
    /// <see cref="DelayOrWake"/>.</summary>
    private Task OnClientDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        if (ShouldWakeOnDisconnect(_enabled, e.ClientWasConnected))
        {
            SetState(MqttConnectionState.Retrying);
            _signals.Drop();

            if (_outage.IsNewLoss())
                _log.Info($"MQTT: the connection to the broker was lost — {MqttClientWiring.DisconnectReason(e)}. " +
                          "Sending resumes when the connection returns.");
        }
        return Task.CompletedTask;
    }

    /// <summary>Forces a reconnect and fresh-state republish after resume from standby, so the
    /// published entities do not linger unavailable. Must be called from the host's power-mode
    /// handler: this class does not subscribe to system events itself, because the unsubscribe
    /// lifetime belongs to the host.</summary>
    public void OnPowerResume()
    {
        if (!_enabled) return;
        // The loop drops and reconnects on its own thread, so this cannot race an in-flight connect.
        _reconnectRequested = true;
        Wake();
    }

    private async Task OnConnectedAsync(CancellationToken ct)
    {
        _outage.Connected();

        // The encryption is named because Automatic can fall back to plain with nobody choosing it,
        // and a downgrade that leaves no trace is one nobody can notice afterwards.
        _log.Info(RememberedEndpoint is { } found
            ? $"MQTT: connected over {MqttStatusText.Name(found.Transport)} on port {found.Port}, " +
              $"{(found.Encrypted == true ? "encrypted" : "not encrypted")}; " +
              $"announcing '{_identity.DeviceId}'."
            : $"MQTT: connected; announcing '{_identity.DeviceId}'.");

        // Evict a superseded device id first, so a consumer never sees both devices at once.
        await _owned.ClearRetiredIdentityAsync(ct).ConfigureAwait(false);

        // Channels dropped while there was no link. Before the announcement, so a value is not
        // stranded under a component the document is about to remove.
        await _channelPublisher.FlushEvictionsAsync(ct).ConfigureAwait(false);

        // The layer above announces before anything is declared online.
        if (_setup.Listener is { } listener)
            await listener.OnConnectedAsync(this, _identity, ct).ConfigureAwait(false);

        await PublishAsync(_availabilityTopic, _setup.OnlinePayload, retain: true, ct: ct).ConfigureAwait(false);
        await SubscribeAsync(ct).ConfigureAwait(false);
        await _channelPublisher.PublishChannelsAsync(_channels.Channels, force: true, ct).ConfigureAwait(false);
    }

    /// <summary>Drops the command wildcard, so nothing published under it is delivered back to this
    /// connection. Best-effort on its own: a broker that will not take the unsubscribe costs the log
    /// some noise, and losing the removal over that would be the worse trade.</summary>
    private async Task UnsubscribeCommandsAsync(CancellationToken ct)
    {
        try
        {
            var options = new MqttClientUnsubscribeOptionsBuilder()
                .WithTopicFilter(MqttTopics.CommandFilter(_setup.TopicRoot, _identity.DeviceId))
                .Build();
            await _client.UnsubscribeAsync(options, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { _log.Error($"MqttConnection.Unsubscribe: {MqttProbe.Describe(ex)}", null); }
    }

    /// <summary>One wildcard covers every command entity; the router resolves by entity id. Anything
    /// a layer above needs to hear rides alongside as its own subscription.</summary>
    private async Task SubscribeAsync(CancellationToken ct)
    {
        var builder = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f
                .WithTopic(MqttTopics.CommandFilter(_setup.TopicRoot, _identity.DeviceId))
                .WithAtLeastOnceQoS());

        foreach (var subscription in _subscriptions)
            builder = builder.WithTopicFilter(f => f
                .WithTopic(subscription.TopicFilter)
                .WithQualityOfServiceLevel(MqttClientWiring.Qos(subscription.Qos)));

        await _client.SubscribeAsync(builder.Build(), ct).ConfigureAwait(false);
    }

    /// <summary>Publishes a payload the caller already has, retained so a consumer has a value
    /// immediately on restart. An unchanged payload is cached but not sent; the cache is updated
    /// while disconnected too, ready for the next connect.</summary>
    public void Publish(string channelKey, string payload) => _channelPublisher.Publish(channelKey, payload);

    /// <summary>Signals that a channel's value may have moved. Coalesced and debounced, so a burst of
    /// signals costs one read of the payload function plus at most one trailing read.</summary>
    public void RequestPublish(string channelKey) => _channelPublisher.RequestPublish(channelKey);

    /// <summary>Signals every declared channel.</summary>
    public void RequestPublish() => _channelPublisher.RequestPublish();

    /// <summary>Publishes every channel's current payload on demand. Nothing is announced and no
    /// config topic is written, so this republishes what the entities already are rather than
    /// re-declaring that they exist. False when nothing reached the broker.</summary>
    /// <remarks>
    /// The dedupe cache is bypassed on purpose, and updated as if the payload had changed. Dropping
    /// an unchanged payload is right for a signal and wrong for a button: pressing it and having
    /// nothing leave the machine is indistinguishable from a dead connection. Which groups are
    /// switched on needs no filter here — a withheld group's entities are not in the channel set.
    /// </remarks>
    public Task<bool> PublishNowAsync() => _channelPublisher.PublishNowAsync();

    /// <summary>Swaps the declared channels and empties the retained topics of the ones that have
    /// gone, in one batched pass. What a group toggle and a rebuilt entity set both come through.</summary>
    /// <remarks>One pass, not one publish per departed entity: at a few dozen entities the sequential
    /// form is a few dozen QoS 1 round trips for a single toggle.</remarks>
    public Task SetChannelsAsync(IEnumerable<MqttChannel> channels, CancellationToken ct = default) =>
        _channelPublisher.SetChannelsAsync(channels, ct);

    /// <summary>Swaps the declared command targets.</summary>
    public void SetCommandTargets(IEnumerable<MqttCommandTarget> targets) => _router.Replace(targets);

    /// <summary>Swaps the subscriptions outside the command tree. They take effect on the next
    /// connect, because a subscription is made once per session.</summary>
    public void SetSubscriptions(IEnumerable<MqttSubscription> subscriptions) =>
        _subscriptions = [.. subscriptions];

    /// <summary>Empties the dedupe cache, so the next pass re-sends every channel.</summary>
    public void ForgetPublished() => _channels.Forget();

    /// <summary>False when the message did not reach the broker. Only a caller the user is watching
    /// needs to know — everything else publishes into the background, where the log is the trace.
    /// With no link there is nothing to send and nothing logged per message: the loss has its own
    /// line, and the next connect resends every channel.</summary>
    public async Task<bool> PublishAsync(
        string topic, string payload, bool retain,
        MqttQos qos = MqttQos.AtLeastOnce, CancellationToken ct = default)
    {
        if (!_client.IsConnected) return false;
        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .WithQualityOfServiceLevel(MqttClientWiring.Qos(qos))
                .Build();

            var result = await _client.PublishAsync(message, ct).ConfigureAwait(false);
            var code = MqttClientWiring.PubackCode(result);
            if (!MqttReason.Delivered(code))
            {
                // The broker took the packet and declined the message — an ACL on the topic, a quota.
                // Silence here is what lets a value that never arrived be recorded as sent.
                _log.Error($"MqttConnection.Publish: '{topic}' was refused by the broker ({code}).", null);
                return false;
            }

            // The one choke point every outbound message passes through.
            Activity.RecordPublish();
            return true;
        }
        catch (Exception ex)
        {
            // A link that dropped between the check and the send is the loss, reported once.
            if (_client.IsConnected)
                _log.Error($"MqttConnection.Publish: '{topic}' could not be sent — {MqttProbe.Describe(ex)}", null);
            return false;
        }
    }

    /// <summary>Publishes a batch with several sends in flight at once, so a pass over every topic
    /// costs one round trip rather than one per message.</summary>
    public async Task<bool> PublishAsync(IEnumerable<MqttMessage> messages, CancellationToken ct = default)
    {
        var batch = messages as IReadOnlyList<MqttMessage> ?? [.. messages];
        if (batch.Count == 0) return true;

        bool[] results = await SendConcurrentlyAsync(
            batch,
            message => PublishAsync(message.Topic, message.Payload, message.Retain, message.Qos, ct),
            ct).ConfigureAwait(false);
        return results.All(ok => ok);
    }

    /// <summary>Runs one send per item with <see cref="MaxInFlightPublishes"/> in flight at a time.
    /// Unbounded concurrency would put the whole set on the wire at once, which a broker answers by
    /// stalling or dropping the session.</summary>
    internal static async Task<bool[]> SendConcurrentlyAsync<T>(
        IReadOnlyList<T> items, Func<T, Task<bool>> send, CancellationToken ct)
    {
        using var slots = new SemaphoreSlim(MaxInFlightPublishes, MaxInFlightPublishes);
        var sends = new Task<bool>[items.Count];
        for (int i = 0; i < items.Count; i++) sends[i] = SendOneAsync(items[i]);
        return await Task.WhenAll(sends).ConfigureAwait(false);

        async Task<bool> SendOneAsync(T item)
        {
            await slots.WaitAsync(ct).ConfigureAwait(false);
            try { return await send(item).ConfigureAwait(false); }
            finally { slots.Release(); }
        }
    }

    /// <summary>Removes the device from the broker and then stops: the document, the availability
    /// topics, every value and every command topic. Explicit and separate, because it takes the whole
    /// device off the receiver, which no ordinary settings change should.</summary>
    /// <returns>False when there was no live link to do it over, so nothing was removed.</returns>
    /// <remarks>Holds the gate, so an apply arriving during the removal waits for it to finish. The
    /// stored parameters still say publishing is on: any later apply starts it again.</remarks>
    public async Task<bool> RemoveDeviceAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_client.IsConnected) return false;
            await StopInternalAsync(remove: true, ct).ConfigureAwait(false);
            SetState(MqttConnectionState.Disabled);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <param name="remove">Whether the device is being removed outright, as against publishing
    /// simply stopping. Only <see cref="RemoveDeviceAsync"/> passes true.</param>
    private async Task StopInternalAsync(bool remove, CancellationToken ct = default)
    {
        _enabled = false;
        _cts?.Cancel();
        try
        {
            if (!_client.IsConnected) return;

            if (remove)
            {
                // The command wildcard goes before the command topics are emptied. This connection is
                // subscribed to its own commands, so the broker hands every clear straight back as a
                // zero-length payload on a command topic — which for a text entity is a value, and for
                // the rest is one refusal in the host's log per command the device owns. Unsubscribing
                // stops the broker sending them at all, rather than filtering them once they arrive,
                // and it misses no genuine command: a removal ends in a disconnect, so nothing is
                // listening a moment later either.
                await UnsubscribeCommandsAsync(ct).ConfigureAwait(false);

                // Empty every retained topic the device owns, payloads and command topics included,
                // so a consumer drops the device and no stuck retained command outlives it. An
                // "offline" publish here would re-retain what this cleared.
                if (_setup.Listener is { } listener)
                    await listener.OnRemovingAsync(this, _identity, ct).ConfigureAwait(false);
                await PublishAsync(
                        _owned.OwnTopics(_identity.DeviceId, _channels.Keys, _router.EntityIds)
                            .Select(MqttMessage.Empty),
                        ct)
                    .ConfigureAwait(false);
            }
            else
            {
                // Stopping keeps the retained announcement, so the device persists and reads as
                // offline. What a normal exit, a switched-off feature and an incomplete configuration
                // all come through.
                await PublishAsync(_availabilityTopic, _setup.OfflinePayload, retain: true, ct: ct)
                    .ConfigureAwait(false);
            }

            await _client.DisconnectAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        catch { /* best-effort teardown */ }
    }

    private void SetState(MqttConnectionState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) == (int)state) return;
        try { StateChanged?.Invoke(state); }
        catch (Exception ex) { _log.Error($"MqttConnection.StateChanged: {MqttProbe.Describe(ex)}", null); }
    }

    /// <summary>Tears the connection down. Synchronous, bounded and idempotent on purpose.</summary>
    /// <remarks>Reached from a host's Exit command on the UI thread, so the teardown runs off it and
    /// inside a budget. The token expires before the wait does, so the wait ends because the work
    /// ended rather than with a QoS 1 publish still in flight into the client's own disposal, waiting
    /// on an acknowledgement a half-dead socket will never send.</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _inbound.Complete();

        var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        try { Task.Run(() => StopUnderGateAsync(stopCts.Token)).Wait(TimeSpan.FromSeconds(1)); }
        catch { /* the process is exiting either way */ }

        _inbound.StopWorker();

        _client.Dispose();
        _cts?.Dispose();
        _inbound.Dispose();
        // The gate is left undisposed on purpose. Nothing here takes its wait handle, so it holds
        // nothing to release, while disposing it would turn a reconcile that is already past the
        // check above into a throw out of its own release.
        // stopCts is left undisposed: its token can outlive this call, and the process is exiting.
    }

    // Under the gate so a reconcile cannot interleave with the stop. A reconcile that holds the gate
    // past the budget, including one that called Dispose itself, leaves this to time out.
    private async Task StopUnderGateAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { await StopInternalAsync(remove: false, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
