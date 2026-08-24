using System.Threading.Channels;
using MQTTnet;

namespace ZeroZero.Mqtt;

/// <summary>The broker connection: the client, the maintain loop, backoff, the candidate sweep, the
/// availability and will, the command channel and its worker, and the retained channels with their
/// dedupe. Never logs the broker password or any payload.</summary>
/// <remarks>Knows nothing of discovery, entities or components. Whatever has to happen on connect
/// above this layer arrives as an <see cref="IMqttConnectionListener"/>.</remarks>
public sealed class MqttConnection : IMqttPublisher, IDisposable
{
    private readonly MqttConnectionSetup _setup;
    private readonly IMqttLog _log;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly MqttChannelSet _channels;
    private readonly MqttCommandRouter _router;
    private volatile IReadOnlyList<MqttSubscription> _subscriptions;

    // Drained on a dedicated worker off the MQTT receive callback, which must return promptly. Single
    // reader: one command's read-modify-write must finish before the next starts.
    private readonly Channel<(string Source, Func<CancellationToken, Task> Run)> _work =
        Channel.CreateUnbounded<(string, Func<CancellationToken, Task>)>(
            new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _workerStop = new();
    private readonly Task _worker;

    private volatile bool _enabled;
    private MqttConnectParameters? _parameters;
    private MqttEndpointMemory? _memory;
    private MqttDeviceIdentity _identity = new("", MqttSettings.DefaultDiscoveryPrefix, "");
    private string _availabilityTopic = "";
    private int _state = (int)MqttConnectionState.Disabled;

    // A superseded identity, held rather than evicted inline because the change can land while
    // disconnected: best-effort now, guaranteed on the next connect. Written under _gate, taken with
    // Interlocked.Exchange.
    private MqttDeviceIdentity? _retiredIdentity;

    // Honoured on the maintain-loop thread, so the forced socket drop cannot race its own connect.
    private volatile bool _reconnectRequested;

    // Cuts the maintain loop's inter-poll delay short. Volatile: swapped for a fresh instance on use.
    private volatile TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource? _cts;

    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(60);

    // Drop detection is event-driven (the client's disconnect event wakes the loop), so this is only
    // a stability re-check and can be long — a device on limited power is not woken every few
    // seconds for nothing.
    private static readonly TimeSpan ConnectedPoll = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(3);

    // What one unreachable transport costs before the next is tried. Same budget the connection check
    // gives each transport, so the two agree on how long "no answer" takes.
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
        _channels = new MqttChannelSet(setup.Channels);
        _router = new MqttCommandRouter(setup.CommandTargets);
        _subscriptions = setup.Subscriptions;

        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += OnClientDisconnectedAsync;
        _worker = Task.Run(ProcessWorkAsync);
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
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!parameters.ShouldRun)
            {
                _parameters = parameters;
                await StopInternalAsync(clearRetained: true).ConfigureAwait(false);
                SetState(MqttConnectionState.Disabled);
                return;
            }

            bool wasRunning = _enabled;
            if (wasRunning && _parameters is { } previous && previous == parameters) return;

            _parameters = parameters;
            _memory = _setup.RecallEndpoint?.Invoke() ?? _memory;

            string machine = Environment.MachineName;
            var previousIdentity = _identity;
            string deviceId = MqttIdentity.Effective(parameters.DeviceId, _setup.TopicRoot, machine);
            _identity = new(
                deviceId,
                string.IsNullOrWhiteSpace(parameters.DiscoveryPrefix)
                    ? MqttSettings.DefaultDiscoveryPrefix
                    : parameters.DiscoveryPrefix.Trim(),
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
                _retiredIdentity = previousIdentity;
            }
            await ClearRetiredIdentityAsync(CancellationToken.None).ConfigureAwait(false);

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
                // Options changed while running — bounce the socket so the loop reconnects with them.
                try { await _client.DisconnectAsync().ConfigureAwait(false); }
                catch { /* the loop retries */ }
            }
        }
        // Apply discards this task, so an unhandled throw would silently disable the feature.
        catch (Exception ex) { _log.Error("MqttConnection.Apply", Sanitise(ex)); }
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

        while (MqttEndpointPlan.NextEndpoint(request, RememberedEndpoint, attempts) is { } candidate)
        {
            var address = MqttEndpoint.Resolve(parameters.Host, candidate);
            MqttProbeResult result;

            if (await CheckListenerAsync(parameters, candidate, address, ct).ConfigureAwait(false)
                is { } unreachable)
            {
                attempts.Add(new(candidate, unreachable));
                continue;
            }

            try
            {
                // MQTTnet hands a refused CONNACK back as a result code rather than throwing, so the
                // code has to be read — otherwise a rejection looks like a live connection until the
                // first publish fails.
                var connack = await _client
                    .ConnectAsync(BuildOptions(parameters, address), ct).ConfigureAwait(false);
                result = MqttProbe.ClassifyConnack(
                    MqttClientWiring.ConnackCode(connack), connack?.ReasonString);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result = MqttProbe.ClassifyConnectException(ex, ct); }

            if (result.Outcome == MqttProbeOutcome.Success)
            {
                RememberEndpoint(request, candidate);
                attempts.Add(new(candidate, result));
                return new(true, attempts);
            }

            // A half-open session from a refused CONNACK would make the next attempt look connected.
            try { if (_client.IsConnected) await _client.DisconnectAsync().ConfigureAwait(false); }
            catch { /* the socket is going away either way */ }
            attempts.Add(new(candidate, result));
        }

        // One line per failed round, naming every candidate tried — the log's whole account of why
        // nothing is publishing. The details are OS or broker text, never a staged credential.
        _log.Error("MqttConnection.Connect: no endpoint connected — " +
            string.Join("; ", attempts.Select(a =>
                $"{MqttStatusText.Name(a.Candidate.Transport)}:{a.Candidate.Port}: {a.Result.Detail}")),
            null);
        return new(false, attempts);
    }

    /// <summary>Whether nothing is listening on an encrypted candidate, so the plan may fall back to
    /// a plain one. Null means carry on and speak MQTT.</summary>
    /// <remarks>
    /// Only run where the answer changes something: an encrypted candidate under Automatic, which is
    /// the one case where a plain candidate sits behind it. The connect path has no stage-1 socket
    /// check of its own, and without one a TLS handshake failure and an empty port are the same
    /// verdict — which is what would send the password to the broker in clear text.
    /// </remarks>
    private static async Task<MqttProbeResult?> CheckListenerAsync(
        MqttConnectParameters parameters, MqttEndpointCandidate candidate,
        MqttEndpointAddress address, CancellationToken ct)
    {
        if (parameters.EncryptionMode != MqttEncryptionMode.Auto || !candidate.Encrypted) return null;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ConnectTimeout);
        var result = await MqttProbe
            .ProbeTcpAsync(address.Host, address.Port, budget.Token, ct).ConfigureAwait(false);

        // Anything other than "nothing is there" is left to the handshake to report properly.
        return result is { Outcome: MqttProbeOutcome.Unreachable } ? result : null;
    }

    private MqttClientOptions BuildOptions(MqttConnectParameters parameters, MqttEndpointAddress address)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithEndpoint(address, parameters.CertificateTrust)
            .WithClientId(_identity.DeviceId)
            .WithCleanSession()
            // MQTTnet pings within this period on an idle link, so the broker will not drop a quiet
            // connection and a silently dead one surfaces rather than lingering "connected".
            .WithKeepAlivePeriod(KeepAlive)
            // Pinned rather than left to the library default: this is what a dead candidate costs
            // before the next is tried, so it has to be a known number.
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
        var backoff = InitialBackoff;
        DateTimeOffset? connectedSince = null;   // when the current live session started

        while (!ct.IsCancellationRequested && _enabled)
        {
            // Modern standby suspends the NIC, so after a resume the socket is often half-dead while
            // the client still reads as connected. A resume is not a flap — reset the backoff.
            if (_reconnectRequested)
            {
                _reconnectRequested = false;
                try { if (_client.IsConnected) await _client.DisconnectAsync().ConfigureAwait(false); }
                catch { /* about to reconnect anyway */ }
                connectedSince = null;
                backoff = InitialBackoff;
            }

            try
            {
                if (!_client.IsConnected && _parameters is { } parameters)
                {
                    // A session that died young is a flap; wait out the escalating backoff before
                    // retrying. A drop of a session that lasted, or a first attempt, reconnects at
                    // once.
                    if (connectedSince is { } since && DateTimeOffset.UtcNow - since < StableConnection)
                    {
                        backoff = NextBackoff(backoff);
                        connectedSince = null;
                        SetState(MqttConnectionState.Retrying);
                        if (!await DelayOrWake(backoff, ct).ConfigureAwait(false)) break;
                    }
                    connectedSince = null;

                    SetState(MqttEndpointPlan.Sweep(parameters.Request, RememberedEndpoint).Count > 1
                        ? MqttConnectionState.Searching
                        : MqttConnectionState.Connecting);

                    var round = await ConnectUsingPlanAsync(parameters, ct).ConfigureAwait(false);
                    if (round.Connected)
                    {
                        await OnConnectedAsync(ct).ConfigureAwait(false);
                        connectedSince = DateTimeOffset.UtcNow;
                        SetState(MqttConnectionState.Connected);
                    }
                    else
                    {
                        // Every candidate failed; wait longer before the next round. A broker that
                        // answered and refused says waiting will not help.
                        backoff = NextBackoff(backoff);
                        SetState(round.Attempts.Any(a => MqttEndpointPlan.Answered(a.Outcome))
                            ? MqttConnectionState.Failed
                            : MqttConnectionState.Retrying);
                    }
                }
                else if (_client.IsConnected
                      && connectedSince is { } stable
                      && DateTimeOffset.UtcNow - stable >= StableConnection)
                {
                    // Proven stable, so the next genuine drop reconnects fast.
                    backoff = InitialBackoff;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.Error("MqttConnection.Connect", Sanitise(ex));
                // The connect sequence can throw with the socket up, leaving neither branch reachable
                // next pass — a healthy-looking connection that never gets its announcement,
                // availability or subscription. Drop it so the next pass retries.
                try { if (_client.IsConnected) await _client.DisconnectAsync().ConfigureAwait(false); }
                catch { /* the next pass reconnects */ }
                backoff = NextBackoff(backoff);
                connectedSince = null;
                SetState(MqttConnectionState.Retrying);
            }

            // Long re-poll while healthy, backoff while failing; a drop or a resume cuts the wait
            // short.
            if (!await DelayOrWake(_client.IsConnected ? ConnectedPoll : backoff, ct).ConfigureAwait(false))
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

    /// <summary>Waits up to <paramref name="delay"/>, early on a wake. False on cancel.</summary>
    private async Task<bool> DelayOrWake(TimeSpan delay, CancellationToken ct)
    {
        var wake = _wake.Task;
        // Linked source so a winning wake cancels the losing delay rather than abandoning its timer.
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var delayTask = Task.Delay(delay, delayCts.Token);
            var winner = await Task.WhenAny(delayTask, wake).ConfigureAwait(false);
            if (winner == wake)
            {
                await delayCts.CancelAsync().ConfigureAwait(false);
                try { await delayTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
                // Re-arm. A signal racing the swap costs at worst one poll interval.
                _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            else
                await winner.ConfigureAwait(false);   // observe cancellation raised by the delay

            return !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException) { return false; }
    }

    private void Wake() => _wake.TrySetResult();

    /// <summary>Wakes the maintain loop on a disconnect so it reconnects and republishes "online" at
    /// once, shrinking the window where a consumer shows the Last Will "offline" while the machine is
    /// alive.</summary>
    private Task OnClientDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        if (ShouldWakeOnDisconnect(_enabled, e.ClientWasConnected))
        {
            SetState(MqttConnectionState.Retrying);
            Wake();
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
        // The encryption is named because Automatic can fall back to plain with nobody choosing it,
        // and a downgrade that leaves no trace is one nobody can notice afterwards.
        _log.Info(RememberedEndpoint is { } found
            ? $"MQTT: connected over {MqttStatusText.Name(found.Transport)} on port {found.Port}, " +
              $"{(found.Encrypted == true ? "encrypted" : "not encrypted")}; " +
              $"announcing '{_identity.DeviceId}'."
            : $"MQTT: connected; announcing '{_identity.DeviceId}'.");

        // Evict a superseded device id first, so a consumer never sees both devices at once.
        await ClearRetiredIdentityAsync(ct).ConfigureAwait(false);

        // The layer above announces before anything is declared online.
        if (_setup.Listener is { } listener)
            await listener.OnConnectedAsync(this, _identity, ct).ConfigureAwait(false);

        await PublishAsync(_availabilityTopic, _setup.OnlinePayload, retain: true, ct: ct).ConfigureAwait(false);
        await SubscribeAsync(ct).ConfigureAwait(false);
        await PublishChannelsAsync(_channels.Channels, force: true, ct).ConfigureAwait(false);
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
    public void Publish(string channelKey, string payload)
    {
        if (!_enabled) return;
        if (_channels.Find(channelKey) is not { } channel) return;
        if (!_channels.Accept(channelKey, payload)) return;
        if (!_client.IsConnected) return;

        _ = SendChannelAsync(channel, Compose(channel, payload), CancellationToken.None);
    }

    /// <summary>Signals that a channel's value may have moved. Coalesced and debounced, so a burst of
    /// signals costs one read of the payload function plus at most one trailing read.</summary>
    public void RequestPublish(string channelKey)
    {
        if (!_enabled) return;
        if (_channels.Signal(channelKey)) _ = Task.Run(() => ChannelLoopAsync(channelKey));
    }

    /// <summary>Signals every declared channel.</summary>
    public void RequestPublish()
    {
        foreach (string key in _channels.Keys) RequestPublish(key);
    }

    /// <summary>Coalesced driver for one channel. The trailing pass is what guarantees the last
    /// snapshot wins: two concurrent reads could otherwise let an older one take the dedupe slot and
    /// strand the newer value.</summary>
    private async Task ChannelLoopAsync(string channelKey)
    {
        do
        {
            _channels.BeginPass(channelKey);
            try
            {
                if (_channels.Find(channelKey) is { } channel)
                {
                    if (channel.Debounce > TimeSpan.Zero)
                        await Task.Delay(channel.Debounce).ConfigureAwait(false);
                    if (_enabled && ComposeCurrent(channel, force: false) is { } message)
                        await SendChannelAsync(channel, message, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { _log.Error("MqttConnection.Publish", Sanitise(ex)); }
            await Task.Yield();   // never hold the pool thread across the repeat check
        }
        while (_channels.ShouldRepeat(channelKey));
    }

    /// <summary>Publishes every channel's current payload on demand. Nothing is announced and no
    /// config topic is written, so this republishes what the entities already are rather than
    /// re-declaring that they exist. False when nothing reached the broker.</summary>
    /// <remarks>
    /// The dedupe cache is bypassed on purpose, and updated as if the payload had changed. Dropping
    /// an unchanged payload is right for a signal and wrong for a button: pressing it and having
    /// nothing leave the machine is indistinguishable from a dead connection. Which groups are
    /// switched on needs no filter here — a withheld group's entities are not in the channel set.
    /// </remarks>
    public async Task<bool> PublishNowAsync()
    {
        if (!IsConnected) return false;
        try
        {
            return await PublishChannelsAsync(_channels.Channels, force: true, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("MqttConnection.PublishNow", Sanitise(ex));
            return false;
        }
    }

    /// <summary>Swaps the declared channels and empties the retained topics of the ones that have
    /// gone, in one batched pass. What a group toggle and a rebuilt entity set both come through.</summary>
    /// <remarks>One pass, not one publish per departed entity: at a few dozen entities the sequential
    /// form is a few dozen QoS 1 round trips for a single toggle.</remarks>
    public async Task SetChannelsAsync(
        IEnumerable<MqttChannel> channels, CancellationToken ct = default)
    {
        var departed = _channels.Replace(channels);
        if (departed.Count == 0 || !IsConnected) return;

        await PublishAsync(
            departed.Select(key =>
                MqttMessage.Empty(MqttTopics.Channel(_setup.TopicRoot, _identity.DeviceId, key))),
            ct).ConfigureAwait(false);
    }

    /// <summary>Swaps the declared command targets.</summary>
    public void SetCommandTargets(IEnumerable<MqttCommandTarget> targets) => _router.Replace(targets);

    /// <summary>Swaps the subscriptions outside the command tree. They take effect on the next
    /// connect, because a subscription is made once per session.</summary>
    public void SetSubscriptions(IEnumerable<MqttSubscription> subscriptions) =>
        _subscriptions = [.. subscriptions];

    /// <summary>Empties the dedupe cache, so the next pass re-sends every channel.</summary>
    public void ForgetPublished() => _channels.Forget();

    /// <summary>Inbound handler. Runs on the MQTT receive callback, so it only judges and enqueues.
    /// Never throws — the MQTT loop must survive a bad payload — and never logs the payload.</summary>
    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var message = new MqttInboundMessage(
                e.ApplicationMessage.Topic,
                e.ApplicationMessage.ConvertPayloadToString() ?? "",
                e.ApplicationMessage.Retain);

            if (RouteCommand(message)) return Task.CompletedTask;

            foreach (var subscription in _subscriptions)
                if (subscription.Matches(message.Topic))
                    Enqueue($"Subscription:{subscription.TopicFilter}",
                            ct => subscription.Handler(message, ct));
        }
        catch (Exception ex) { _log.Error("MqttConnection.OnMessage", Sanitise(ex)); }
        return Task.CompletedTask;
    }

    /// <summary>True when the message belonged to the command subtree, whatever became of it.</summary>
    private bool RouteCommand(MqttInboundMessage message)
    {
        var routing = _router.Route(
            _setup.TopicRoot, _identity.DeviceId, message.Topic, message.Retained, message.Payload);

        if (routing.EntityId is not { } entityId) return false;

        if (routing.Verdict is { Outcome: MqttCommandOutcome.Accepted, Run: { } run })
        {
            // Recorded on acceptance, not on completion: the status line answers "is the broker
            // reaching us".
            Activity.RecordCommand(entityId);
            Enqueue($"Command:{entityId}", run);
            return true;
        }

        // Not acted on. The facts are the module's; the wording is the entity's, carried verbatim.
        _setup.CommandRefused?.Invoke(new MqttCommandRefusal(
            DateTimeOffset.UtcNow, entityId, routing.Verdict.Outcome, routing.Verdict.Detail));
        return true;
    }

    private void Enqueue(string source, Func<CancellationToken, Task> run) =>
        _work.Writer.TryWrite((source, run));   // unbounded and non-blocking; the worker drains in order

    /// <summary>Drains the queue one item at a time, so a blocking read-modify-write completes before
    /// the next starts. Ends when the writer is completed.</summary>
    private async Task ProcessWorkAsync()
    {
        await foreach (var (source, run) in _work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                // Any republish happens through the application's own change signal, not a publish
                // here.
                await run(_workerStop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_workerStop.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.Error($"MqttConnection.{source}", Sanitise(ex)); }
        }
    }

    /// <summary>Empties every retained topic a superseded identity owned, so a consumer deletes the
    /// old device rather than leaving a ghost. No-op while disconnected: the next connect runs it.</summary>
    private async Task ClearRetiredIdentityAsync(CancellationToken ct)
    {
        if (!_client.IsConnected) return;
        if (Interlocked.Exchange(ref _retiredIdentity, null) is not { } retired) return;

        if (_setup.Listener is { } listener)
            await listener.OnIdentityRetiredAsync(this, retired, ct).ConfigureAwait(false);

        await PublishAsync(OwnTopics(retired.DeviceId).Select(MqttMessage.Empty), ct).ConfigureAwait(false);
    }

    /// <summary>Every retained topic this layer owns under one device id: the availability topic and
    /// each declared channel. They belong in an eviction because they are published retained too —
    /// leave them out and a payload is stranded on the broker under the abandoned id.</summary>
    private IEnumerable<string> OwnTopics(string deviceId)
    {
        yield return MqttTopics.Availability(_setup.TopicRoot, deviceId);
        foreach (string key in _channels.Keys)
            yield return MqttTopics.Channel(_setup.TopicRoot, deviceId, key);
    }

    /// <summary>False when the message did not reach the broker. Only a caller the user is watching
    /// needs to know — everything else publishes into the background, where the log is the trace.</summary>
    public async Task<bool> PublishAsync(
        string topic, string payload, bool retain,
        MqttQos qos = MqttQos.AtLeastOnce, CancellationToken ct = default)
    {
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
        catch (Exception ex) { _log.Error("MqttConnection.Publish", Sanitise(ex)); return false; }
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
    private static async Task<bool[]> SendConcurrentlyAsync<T>(
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

    /// <summary>Reads every channel and publishes what each one's current reading calls for, as one
    /// batch. A send that fails takes its dedupe entry with it, so the next pass re-sends.</summary>
    private async Task<bool> PublishChannelsAsync(
        IReadOnlyList<MqttChannel> channels, bool force, CancellationToken ct)
    {
        var batch = new List<(MqttChannel Channel, MqttMessage Message)>(channels.Count);
        foreach (var channel in channels)
            if (ComposeCurrent(channel, force) is { } message) batch.Add((channel, message));

        if (batch.Count == 0) return false;

        bool[] results = await SendConcurrentlyAsync(
            batch, entry => SendChannelAsync(entry.Channel, entry.Message, ct), ct).ConfigureAwait(false);
        return results.Any(ok => ok);
    }

    private async Task<bool> SendChannelAsync(MqttChannel channel, MqttMessage message, CancellationToken ct)
    {
        bool sent = await PublishAsync(message.Topic, message.Payload, message.Retain, message.Qos, ct)
            .ConfigureAwait(false);

        // A payload that never reached the broker must not be remembered as the topic's value, or the
        // next pass dedupes it away and the topic stays wrong until the value happens to change.
        if (!sent) _channels.Forget(channel.Key);
        return sent;
    }

    private MqttMessage Compose(MqttChannel channel, string payload) =>
        new(MqttTopics.Channel(_setup.TopicRoot, _identity.DeviceId, channel.Key),
            payload, channel.Retain, channel.Qos);

    /// <summary>What one channel's current reading calls for, or null when it calls for nothing. The
    /// dedupe slot is taken here, so the caller only has to send.</summary>
    private MqttMessage? ComposeCurrent(MqttChannel channel, bool force)
    {
        var (status, payload) = ReadPayload(channel);
        switch (status)
        {
            case MqttPayloadStatus.Value:
                if (force) _channels.Force(channel.Key, payload!);
                else if (!_channels.Accept(channel.Key, payload!)) return null;
                return Compose(channel, payload!);

            case MqttPayloadStatus.None:
                // A channel whose producer has a first reading to wait for keeps what it last
                // published, and sends it again on connect so a consumer that restarted has it.
                if (channel.RepublishLastOnConnect)
                    return force && _channels.LastPayload(channel.Key) is { Length: > 0 } last
                        ? Compose(channel, last)
                        : null;

                // Otherwise no reading means an empty topic: a consumer connecting later sees nothing
                // rather than a value of unknown age. Deduped, so it is emptied once and not on
                // every pass.
                return _channels.Accept(channel.Key, "") ? Compose(channel, "") : null;

            default:
                // The reader failed, so nothing is known about the current value. What stands, stands,
                // and the next pass tries again — emptying the topic here would assert "no value" on
                // the strength of a bug in the reader.
                return null;
        }
    }

    // A payload function is the application's code on the module's thread, so a throw from one must
    // not take the connect sequence or the publishing loop with it. A throw and a null are told
    // apart deliberately: null is "no current reading", a throw is "the reading could not be taken".
    private (MqttPayloadStatus Status, string? Payload) ReadPayload(MqttChannel channel)
    {
        try
        {
            return channel.Payload() is { } payload
                ? (MqttPayloadStatus.Value, payload)
                : (MqttPayloadStatus.None, null);
        }
        catch (Exception ex)
        {
            _log.Error($"MqttConnection.Payload:{channel.Key}", Sanitise(ex));
            return (MqttPayloadStatus.Failed, null);
        }
    }

    private async Task StopInternalAsync(bool clearRetained, CancellationToken ct = default)
    {
        _enabled = false;
        _cts?.Cancel();
        try
        {
            if (!_client.IsConnected) return;

            if (clearRetained)
            {
                // Publishing turned off: empty every retained topic the device owns, payloads
                // included, so a consumer drops the device. An "offline" publish here would
                // re-retain what this cleared.
                if (_setup.Listener is { } listener)
                    await listener.OnStoppingAsync(this, _identity, ct).ConfigureAwait(false);
                await PublishAsync(OwnTopics(_identity.DeviceId).Select(MqttMessage.Empty), ct)
                    .ConfigureAwait(false);
            }
            else
            {
                // A normal exit keeps the retained announcement, so the device persists.
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
        catch (Exception ex) { _log.Error("MqttConnection.StateChanged", Sanitise(ex)); }
    }

    // Keeps a thrown broker error from carrying the password into the log: type and message only.
    private static Exception Sanitise(Exception ex) => new($"{ex.GetType().Name}: {ex.Message}");

    /// <summary>Tears the connection down. Synchronous and bounded on purpose.</summary>
    /// <remarks>Reached from a host's Exit command on the UI thread, so the teardown runs off it and
    /// inside a budget. The token expires before the wait does, so the wait ends because the work
    /// ended rather than with a QoS 1 publish still in flight into the client's own disposal, waiting
    /// on an acknowledgement a half-dead socket will never send.</remarks>
    public void Dispose()
    {
        try { _work.Writer.TryComplete(); } catch { /* already completed */ }

        var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        try { Task.Run(() => StopInternalAsync(clearRetained: false, stopCts.Token)).Wait(TimeSpan.FromSeconds(1)); }
        catch { /* the process is exiting either way */ }

        _workerStop.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { /* a handler outliving the budget */ }

        _client.Dispose();
        _cts?.Dispose();
        _workerStop.Dispose();
        _gate.Dispose();
        // stopCts is left undisposed: its token can outlive this call, and the process is exiting.
    }
}
