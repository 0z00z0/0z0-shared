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

    /// <summary>Where what was published is written down. Required, and with no default, because every
    /// alternative is a choice with consequences: see <see cref="DiscoveryLedgerFile.In"/> for the
    /// durable form and <see cref="TransientLedgerStore"/> for what is given up without it.</summary>
    public required IDiscoveryLedgerStore Ledger { get; init; }

    /// <summary>The publish groups, or null for a consumer that declares none.</summary>
    public PublishGroupSet? Groups { get; init; }

    /// <summary>Entities withdrawn in an earlier version of the consumer, whose retained
    /// per-component configs are emptied once and then written down.</summary>
    public IReadOnlyList<RetiredEntity> Retired { get; init; } = [];

    /// <summary>Entities moving from their own single-component config into the device document,
    /// keeping everything the user set on them.</summary>
    public IReadOnlyList<MigratingEntity> Migrating { get; init; } = [];

    /// <summary>Value topics an earlier version of the consumer left retained under this identity,
    /// emptied once and then written down. What reaches a key no entity declaration composes — one a
    /// hand-rolled or shared-payload predecessor published on under this same topic root.</summary>
    public IReadOnlyList<RetiredChannel> RetiredChannels { get; init; } = [];

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
/// and empties everything the device owns when the device is removed.</summary>
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

    /// <exception cref="ArgumentException">An entity is declared both retired and migrating, a retired
    /// id names an entity the set still publishes, or a retired channel key is unusable or names an
    /// entity that publishes on that very topic.</exception>
    public DiscoveryPublisher(DiscoveryPublisherSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        Validate(setup.Entities, setup);

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
    /// <exception cref="ArgumentException">The new set contradicts the declared retirements.</exception>
    public Task SetEntitiesAsync(MqttEntitySet entities, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        Validate(entities, _setup);
        lock (_gate) _entities = entities;
        return AnnounceAsync(force: false, ct);
    }

    /// <summary>Re-reads what the entities currently say and re-announces if it has changed. What a
    /// select whose option list moved needs, and what a group toggle comes through.</summary>
    public void Republish() => _ = Guard(nameof(Republish), RepublishAsync);

    /// <inheritdoc cref="Republish"/>
    public Task RepublishAsync(CancellationToken ct = default) => AnnounceAsync(force: false, ct);

    /// <summary>Removes the device outright: the document, the availability topics and every value.
    /// Deliberately explicit, and never what switching publishing off does — this takes the whole
    /// device off the receiver.</summary>
    public Task RemoveDeviceAsync(CancellationToken ct = default)
    {
        var (publisher, identity) = Target();
        return publisher is null ? Task.CompletedTask : WithdrawAsync(publisher, identity, ct);
    }

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
        await AnnounceAsync(force: true, ct).ConfigureAwait(false);
    }

    async Task IMqttConnectionListener.OnRemovingAsync(
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
    private async Task AnnounceAsync(bool force, CancellationToken ct)
    {
        await _pass.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // One snapshot for the whole pass, so it cannot announce one entity under the old group
            // state and the next under the new. The record goes in too: an entity whose capability
            // could not be read keeps what it was, rather than one failed call deciding it.
            var groups = _setup.Groups?.Snapshot();
            var entities = Entities;
            var (publisher, identity) = Target();
            var ledger = _setup.Ledger.Read();

            var (published, withheld) = entities.Resolve(groups, ledger.Find(identity.DeviceId));

            _setup.SetCommandTargets?.Invoke(MqttEntitySet.CommandTargets(published));
            if (_setup.SetChannelsAsync is { } setChannels)
                await setChannels(MqttEntitySet.Channels(published), ct).ConfigureAwait(false);

            if (publisher is null || identity.DeviceId.Length == 0 || !_setup.IsConnected()) return;

            var plan = DiscoveryPlan.Announce(
                ledger, _setup.TopicRoot, identity, _setup.Device, _setup.Origin,
                published, withheld, _setup.Retired, _setup.Migrating, _setup.RetiredChannels,
                _setup.OnlinePayload, _setup.OfflinePayload);

            bool landed = await publisher.PublishAsync(plan.Evictions, ct).ConfigureAwait(false);

            if (force || _announced != (plan.ConfigTopic, plan.Document))
            {
                landed &= await publisher
                    .PublishAsync(plan.ConfigTopic, plan.Document, retain: true, ct: ct)
                    .ConfigureAwait(false);
                _announced = landed ? (plan.ConfigTopic, plan.Document) : null;
            }

            // Only once the document has landed. Everything in the sweep removes something the new
            // document is meant to have taken over first — a migrating entity's old config, the old
            // address after a prefix change, the values of components it has just removed — so sending
            // it after a document that never arrived would remove without replacing.
            if (landed) landed = await publisher.PublishAsync(plan.Sweep, ct).ConfigureAwait(false);

            // Only what reached the broker is written down. A pass that half-landed leaves the record
            // as it was, so the next connect evicts what this one failed to — and re-sends a migration
            // flag that never arrived rather than recording it as done.
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
                _setup.Ledger.Read(), _setup.TopicRoot, identity, Entities.All,
                _setup.Retired, _setup.Migrating, _setup.RetiredChannels);

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
        await AnnounceAsync(force: true, ct).ConfigureAwait(false);
    }

    private void OnGroupsChanged() => Republish();

    private IReadOnlyList<MqttEntity> Announced() => Entities.Published(_setup.Groups?.Snapshot());

    private (IMqttPublisher? Publisher, MqttDeviceIdentity Identity) Target()
    {
        lock (_gate) return (_publisher, _identity);
    }

    private void Store(DiscoveryLedger ledger) =>
        _setup.Ledger.Update(stored => stored.Devices = ledger.Devices);

    private static void Validate(MqttEntitySet entities, DiscoveryPublisherSetup setup)
    {
        if (DiscoveryDeclaration.Validate(
                entities, setup.Retired, setup.Migrating, setup.RetiredChannels) is { } error)
            throw new ArgumentException(error, nameof(entities));
    }

    // The callers are fire-and-forget, so an unhandled throw would silently stop the announcement.
    private async Task Guard(string source, Func<CancellationToken, Task> work)
    {
        try { await work(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _log.Error($"{nameof(DiscoveryPublisher)}.{source}", Sanitise(ex)); }
    }

    private static Exception Sanitise(Exception ex) => new($"{ex.GetType().Name}: {ex.Message}");
}
