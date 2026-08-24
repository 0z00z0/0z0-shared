namespace ZeroZero.Mqtt.Discovery;

/// <summary>Everything the publisher needs that does not vary with the broker settings.</summary>
public sealed record DiscoveryPublisherSetup
{
    /// <summary>Whether there is a live link. A pass outside the connect sequence is skipped while
    /// disconnected: the next connect runs the whole sequence anyway.</summary>
    public required Func<bool> IsConnected { get; init; }

    /// <summary>The application's own segment at the head of every state and command topic. The same
    /// value the connection was given.</summary>
    public required string TopicRoot { get; init; }

    public required DiscoveryDevice Device { get; init; }

    public required DiscoveryOrigin Origin { get; init; }

    /// <summary>What is published now. Replaceable at runtime through
    /// <see cref="DiscoveryPublisher.SetEntitiesAsync"/>.</summary>
    public required MqttEntitySet Entities { get; init; }

    /// <summary>The publish groups, or null for a consumer that declares none.</summary>
    public PublishGroupSet? Groups { get; init; }

    /// <summary>Entities withdrawn in an earlier version of the consumer, whose retained
    /// per-component configs must go on being emptied.</summary>
    public IReadOnlyList<RetiredEntity> Retired { get; init; } = [];

    /// <summary>Where what was published is written down. The default keeps it for the life of the
    /// process, which is enough for a fixed entity table and not enough for one that changes: an
    /// entity removed while the application was closed is evicted only if the record outlived it.</summary>
    public IDiscoveryLedgerStore Ledger { get; init; } = new TransientLedgerStore();

    /// <summary>Where the announced channel set is handed to the connection. Null for a consumer that
    /// declares its channels itself.</summary>
    public Func<IReadOnlyList<MqttChannel>, CancellationToken, Task>? SetChannelsAsync { get; init; }

    /// <summary>Where the announced command targets are handed to the connection.</summary>
    public Action<IReadOnlyList<MqttCommandTarget>>? SetCommandTargets { get; init; }

    /// <summary>The availability payloads the document declares at its root. The same values the
    /// connection was given.</summary>
    public string OnlinePayload { get; init; } = "online";

    public string OfflinePayload { get; init; } = "offline";

    /// <summary>What a receiver publishes on its own status topic when it comes back. Anything else
    /// there is its will, and means the opposite.</summary>
    public string BirthPayload { get; init; } = "online";

    /// <summary>The longest a birth message waits before the republish it triggers. The actual wait is
    /// a random slice of it, so a fleet of machines does not answer one restarted receiver at
    /// once.</summary>
    public TimeSpan BirthRepublishDelay { get; init; } = TimeSpan.FromSeconds(30);

    public IMqttLog Log { get; init; } = NullMqttLog.Instance;
}

/// <summary>Announces the device document on connect, re-announces it when what is announced changes,
/// and empties everything the device owns when publishing stops.</summary>
/// <remarks>
/// The connection knows nothing of this: it runs the layer above at three points through
/// <see cref="IMqttConnectionListener"/>, and hands it a publisher each time. Nothing here holds an
/// MQTT client, so the whole of it is exercisable against a recording double.
/// </remarks>
public sealed class DiscoveryPublisher : IMqttConnectionListener, IDisposable
{
    private readonly DiscoveryPublisherSetup _setup;
    private readonly IMqttLog _log;

    // One pass at a time. A group toggle, a rebuilt entity set and a connect can arrive together, and
    // two passes interleaving would write a ledger describing neither.
    private readonly SemaphoreSlim _pass = new(1, 1);
    private readonly Lock _gate = new();

    private MqttEntitySet _entities;
    private IMqttPublisher? _publisher;
    private MqttDeviceIdentity _identity = new("", MqttSettings.DefaultDiscoveryPrefix, "");

    // The document as it stands on the broker, so an unchanged one is not re-sent. Keyed on the topic
    // because a change of identity is a different document at a different address.
    private (string Topic, string Json)? _announced;

    public DiscoveryPublisher(DiscoveryPublisherSetup setup)
    {
        _setup = setup;
        _log = setup.Log;
        _entities = setup.Entities;

        if (setup.Groups is { } groups) groups.Changed += OnGroupsChanged;
    }

    /// <summary>The entity set in force.</summary>
    public MqttEntitySet Entities
    {
        get { lock (_gate) return _entities; }
    }

    /// <summary>The retained channels the announced entities publish on, for a consumer composing the
    /// connection's setup before there is anything to announce to.</summary>
    public IReadOnlyList<MqttChannel> Channels() => MqttEntitySet.Channels(Announced());

    /// <summary>The command targets the announced entities route to.</summary>
    public IReadOnlyList<MqttCommandTarget> CommandTargets() => MqttEntitySet.CommandTargets(Announced());

    /// <summary>Rebuilds the announcement around a new entity set: the channels, the command targets
    /// and the document in one pass, and the state topics of the entities that have gone are
    /// emptied.</summary>
    public void SetEntities(MqttEntitySet entities) =>
        _ = Guard(nameof(SetEntities), ct => SetEntitiesAsync(entities, ct));

    /// <inheritdoc cref="SetEntities"/>
    public Task SetEntitiesAsync(MqttEntitySet entities, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        lock (_gate) _entities = entities;
        return AnnounceAsync(includeRetired: false, force: false, ct);
    }

    /// <summary>Re-reads what the entities currently say and re-announces if it has changed. What a
    /// select whose option list moved needs, and what a group toggle comes through.</summary>
    public void Republish() => _ = Guard(nameof(Republish), RepublishAsync);

    /// <inheritdoc cref="Republish"/>
    public Task RepublishAsync(CancellationToken ct = default) =>
        AnnounceAsync(includeRetired: false, force: false, ct);

    /// <summary>The subscription that answers a receiver's birth message. Wired into the connection's
    /// own subscription list by a consumer that wants a receiver restarting with no retained state to
    /// find the device again without waiting for a reconnect.</summary>
    public MqttSubscription BirthMessage(string discoveryPrefix) =>
        new(DiscoveryTopics.Status(discoveryPrefix), OnBirthMessageAsync);

    async Task IMqttConnectionListener.OnConnectedAsync(
        IMqttPublisher publisher, MqttDeviceIdentity identity, CancellationToken ct)
    {
        lock (_gate) { _publisher = publisher; _identity = identity; }

        // Forced: the retained set on the broker is not this process's to assume anything about. It
        // may have been cleared, or written by an older version, or by another machine sharing an id.
        await AnnounceAsync(includeRetired: true, force: true, ct).ConfigureAwait(false);
    }

    async Task IMqttConnectionListener.OnStoppingAsync(
        IMqttPublisher publisher, MqttDeviceIdentity identity, CancellationToken ct)
    {
        lock (_gate) { _publisher = publisher; _identity = identity; }
        await WithdrawAsync(publisher, identity, ct).ConfigureAwait(false);
    }

    Task IMqttConnectionListener.OnIdentityRetiredAsync(
        IMqttPublisher publisher, MqttDeviceIdentity retired, CancellationToken ct) =>
        WithdrawAsync(publisher, retired, ct);

    public void Dispose()
    {
        if (_setup.Groups is { } groups) groups.Changed -= OnGroupsChanged;
        _pass.Dispose();
    }

    /// <summary>One announcement pass: the projection to the connection, then the eviction, the
    /// document and the sweep, then the record of what landed.</summary>
    private async Task AnnounceAsync(bool includeRetired, bool force, CancellationToken ct)
    {
        await _pass.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // One snapshot for the whole pass, so it cannot announce one entity under the old group
            // state and the next under the new.
            var groups = _setup.Groups?.Snapshot();
            var entities = Entities;
            var published = entities.Published(groups);

            _setup.SetCommandTargets?.Invoke(MqttEntitySet.CommandTargets(published));
            if (_setup.SetChannelsAsync is { } setChannels)
                await setChannels(MqttEntitySet.Channels(published), ct).ConfigureAwait(false);

            var (publisher, identity) = Target();
            if (publisher is null || identity.DeviceId.Length == 0 || !_setup.IsConnected()) return;

            var plan = DiscoveryPlan.Announce(
                _setup.Ledger.Read(), _setup.TopicRoot, identity, _setup.Device, _setup.Origin,
                published, _setup.Retired, _setup.OnlinePayload, _setup.OfflinePayload, includeRetired);

            bool landed = await publisher.PublishAsync(plan.Evictions, ct).ConfigureAwait(false);

            if (force || _announced != (plan.ConfigTopic, plan.Document))
            {
                landed &= await publisher
                    .PublishAsync(plan.ConfigTopic, plan.Document, retain: true, ct: ct)
                    .ConfigureAwait(false);
                _announced = landed ? (plan.ConfigTopic, plan.Document) : null;
            }

            landed &= await publisher.PublishAsync(plan.Sweep, ct).ConfigureAwait(false);

            // Only what reached the broker is written down. A pass that half-landed leaves the record
            // as it was, so the next connect evicts what this one failed to.
            if (landed) Store(plan.Ledger);
        }
        finally { _pass.Release(); }
    }

    /// <summary>Everything one identity owns, emptied, and its record dropped.</summary>
    private async Task WithdrawAsync(
        IMqttPublisher publisher, MqttDeviceIdentity identity, CancellationToken ct)
    {
        if (identity.DeviceId.Length == 0) return;

        await _pass.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (messages, ledger) = DiscoveryPlan.Withdraw(
                _setup.Ledger.Read(), identity.DiscoveryPrefix, identity.DeviceId, _setup.Retired);

            if (await publisher.PublishAsync(messages, ct).ConfigureAwait(false)) Store(ledger);
            _announced = null;
        }
        finally { _pass.Release(); }
    }

    /// <summary>A receiver that has come back. Its own will lands on the same topic and means the
    /// opposite, so only the birth payload is answered.</summary>
    private async Task OnBirthMessageAsync(MqttInboundMessage message, CancellationToken ct)
    {
        if (!string.Equals(message.Payload, _setup.BirthPayload, StringComparison.Ordinal)) return;

        var delay = _setup.BirthRepublishDelay;
        if (delay > TimeSpan.Zero)
            await Task.Delay(Random.Shared.NextDouble() * delay, ct).ConfigureAwait(false);

        _log.Info("MQTT: the receiver announced itself; re-announcing the device.");
        await AnnounceAsync(includeRetired: true, force: true, ct).ConfigureAwait(false);
    }

    private void OnGroupsChanged() => Republish();

    private IReadOnlyList<MqttEntity> Announced() => Entities.Published(_setup.Groups?.Snapshot());

    private (IMqttPublisher? Publisher, MqttDeviceIdentity Identity) Target()
    {
        lock (_gate) return (_publisher, _identity);
    }

    private void Store(DiscoveryLedger ledger) =>
        _setup.Ledger.Update(stored => stored.Devices = ledger.Devices);

    // The callers are fire-and-forget, so an unhandled throw would silently stop the announcement.
    private async Task Guard(string source, Func<CancellationToken, Task> work)
    {
        try { await work(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _log.Error($"{nameof(DiscoveryPublisher)}.{source}", Sanitise(ex)); }
    }

    private static Exception Sanitise(Exception ex) => new($"{ex.GetType().Name}: {ex.Message}");
}
