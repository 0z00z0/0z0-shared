namespace ZeroZero.Mqtt.Discovery;

/// <summary>What one announcement pass puts on the wire, in the order it has to happen.</summary>
/// <param name="Evictions">Sent before the document: identities this installation no longer publishes
/// under, the retirements not yet made, and the migration flags that hand a single-component item over
/// while keeping its registry entry. Emptying a superseded identity first is what stops a receiver
/// holding two devices at once.</param>
/// <param name="Document">The retained device document.</param>
/// <param name="Sweep">Sent after the document: the single-component topics a migration has finished
/// with, and the state topics nothing retains a value on any more. After, because a value outliving
/// its component for a moment is harmless and a component arriving before its own removal is not, and
/// because emptying a migrating entity's old topic before the document has taken it over is a
/// deletion.</param>
/// <param name="Ledger">What the record becomes once the pass has landed.</param>
internal sealed record DiscoveryPass(
    IReadOnlyList<MqttMessage> Evictions,
    string ConfigTopic,
    string Document,
    IReadOnlyList<MqttMessage> Sweep,
    DiscoveryLedger Ledger);

/// <summary>Reconciles what was published against what is to be published. Pure: the ledger and the
/// entity lists go in, the messages and the next ledger come out.</summary>
/// <remarks>
/// An entity stops being published for reasons that mean entirely different things to a receiver, and
/// the record carries which: <b>deleted</b> is gone from the entity table and is removed;
/// <b>withheld</b> is a group switched off or an <see cref="MqttEntity.Include"/> currently false, is
/// reversible, and is announced unavailable rather than removed; <b>migrating</b> is an item handed
/// over from single-component discovery; <b>retired</b> is an id the consumer declares no longer
/// exists anywhere.
/// <para>The distinction is about saying what is true, not about rescuing anything. A receiver files
/// what the user set against the unique id and gives it back when the entity returns, so nothing here
/// restores, re-attaches or reconciles on its behalf.</para>
/// </remarks>
internal static class DiscoveryPlan
{
    /// <summary>The pass that announces <paramref name="published"/>, holds
    /// <paramref name="withheld"/> unavailable, and removes what the entity table no longer
    /// contains.</summary>
    public static DiscoveryPass Announce(
        DiscoveryLedger ledger,
        string topicRoot,
        MqttDeviceIdentity identity,
        DiscoveryDevice device,
        DiscoveryOrigin origin,
        IReadOnlyList<MqttEntity> published,
        IReadOnlyList<MqttEntity> withheld,
        IReadOnlyList<RetiredEntity> retired,
        IReadOnlyList<MigratingEntity> migrating,
        string onlinePayload,
        string offlinePayload)
    {
        string configTopic = DiscoveryTopics.Device(identity.DiscoveryPrefix, identity.DeviceId);
        string withheldTopic = MqttTopics.WithheldAvailability(topicRoot, identity.DeviceId);
        var next = ledger.Copy();

        var evictions = new List<MqttMessage>();
        var sweep = new List<MqttMessage>();

        // Identity is the device id, not the address the document sits at. A moved discovery prefix
        // leaves every unique id, the availability topic and every state topic exactly where they
        // were, so abandoning on it would empty the live device's own topics — the availability the
        // will owns and every current value — and take the device off the receiver on the way past.
        foreach (var abandoned in next.Devices
            .Where(d => !string.Equals(d.DeviceId, identity.DeviceId, StringComparison.Ordinal))
            .ToList())
        {
            evictions.AddRange(Abandon(abandoned));
            next.Devices.Remove(abandoned);
        }

        var current = next.Find(identity.DeviceId);
        if (current is null)
            next.Devices.Add(current = new PublishedDevice
            {
                DeviceId = identity.DeviceId,
                ConfigTopic = configTopic,
            });

        // The prefix moved. The new document is published first and the old address cleared after, so
        // the announcement always exists somewhere: clearing first would leave a window with no device
        // at all, and leaving the old address alone for good leaves retained configs a receiver later
        // pointed at that prefix would resurrect with nothing left to evict them.
        if (current.ConfigTopic is { Length: > 0 } previous
            && !string.Equals(previous, configTopic, StringComparison.Ordinal))
            sweep.Add(MqttMessage.Empty(previous));

        Retire(current, identity, retired, evictions);
        Migrate(current, identity, migrating, evictions, sweep);

        var recorded = current.Entities;
        var known = recorded.Select(e => e.EntityId).ToHashSet(StringComparer.Ordinal);

        // Only what has already been announced is held unavailable. An entity whose group has never
        // been switched on has no registry entry to protect, and announcing one would create the very
        // thing the user declined.
        var unavailable = withheld.Where(e => known.Contains(e.EntityId)).ToList();
        var reversible = unavailable.Select(e => e.EntityId).ToHashSet(StringComparer.Ordinal);

        var wanted = published.Select(e => Record(topicRoot, identity.DeviceId, e)).ToList();
        var live = wanted.Select(e => e.EntityId).ToHashSet(StringComparer.Ordinal);

        // Permanent removal, and only for what the entity table no longer contains.
        var removed = recorded
            .Where(e => !live.Contains(e.EntityId) && !reversible.Contains(e.EntityId))
            .ToList();

        List<PublishedEntity> entities =
        [
            .. wanted,
            .. unavailable.Select(e => new PublishedEntity
            {
                EntityId = e.EntityId,
                Platform = e.Platform,
                // Withheld entities publish nothing, so nothing of theirs is retained.
                StateTopic = "",
                Withheld = true,
            }),
        ];

        // Compared as topics rather than as entity ids: an entity that keeps its id and loses its
        // state topic — withheld, or turned non-retaining — would otherwise strand the value it left
        // behind, and it would survive every later pass because its id is still in the record.
        var keptTopics = entities
            .Where(e => e.StateTopic.Length > 0)
            .Select(e => e.StateTopic)
            .ToHashSet(StringComparer.Ordinal);
        sweep.AddRange(recorded
            .Where(e => e.StateTopic.Length > 0 && !keptTopics.Contains(e.StateTopic))
            .Select(e => MqttMessage.Empty(e.StateTopic)));

        // Before the document, so nothing points at a topic that has yet to say offline.
        if (unavailable.Count > 0
            && !string.Equals(current.WithheldTopic, withheldTopic, StringComparison.Ordinal))
        {
            evictions.Add(new MqttMessage(withheldTopic, offlinePayload, Retain: true));
            current.WithheldTopic = withheldTopic;
        }

        current.ConfigTopic = configTopic;
        current.AvailabilityTopic = MqttTopics.Availability(topicRoot, identity.DeviceId);
        current.Entities = entities;

        return new DiscoveryPass(
            evictions,
            configTopic,
            DiscoveryDocument.Build(
                topicRoot, identity, device, origin, published, unavailable, removed,
                onlinePayload, offlinePayload),
            sweep,
            next);
    }

    /// <summary>The pass that removes one identity outright: the whole device goes, and everything it
    /// retained goes with it.</summary>
    /// <remarks>Explicit and separate from switching publishing off, which leaves the device in place
    /// and merely says it is offline. This deletes every registry entry the device owns, and with them
    /// the names, entity ids and areas the user chose.</remarks>
    /// <param name="known">Every entity the table currently declares, announced or not. What lets a
    /// removal empty the availability and state topics of an installation whose record was lost or
    /// never written.</param>
    public static (IReadOnlyList<MqttMessage> Messages, DiscoveryLedger Ledger) Withdraw(
        DiscoveryLedger ledger,
        string topicRoot,
        MqttDeviceIdentity identity,
        IReadOnlyList<MqttEntity> known,
        IReadOnlyList<RetiredEntity> retired,
        IReadOnlyList<MigratingEntity> migrating)
    {
        var next = ledger.Copy();
        var record = next.Find(identity.DeviceId);

        // Composed where the record has nothing to say. A first run, or a record that was lost, must
        // still leave nothing behind: clearing only the document would strand the availability topic
        // and every value under it.
        var topics = new List<string>
        {
            record?.ConfigTopic is { Length: > 0 } config
                ? config
                : DiscoveryTopics.Device(identity.DiscoveryPrefix, identity.DeviceId),
            record?.AvailabilityTopic is { Length: > 0 } availability
                ? availability
                : MqttTopics.Availability(topicRoot, identity.DeviceId),
            MqttTopics.WithheldAvailability(topicRoot, identity.DeviceId),
        };

        topics.AddRange(record?.Entities.Where(e => e.StateTopic.Length > 0).Select(e => e.StateTopic) ?? []);
        topics.AddRange(known
            .Where(e => e.HasState && e.Retain)
            .Select(e => MqttTopics.Channel(topicRoot, identity.DeviceId, e.EntityId)));

        // The command subtree too. This layer publishes nothing there, but something else can leave a
        // retained command standing, and a device removed with one still on the broker gets it
        // redelivered the moment anything subscribes again.
        topics.AddRange(known
            .Where(e => e.IsCommand)
            .Select(e => MqttTopics.Command(topicRoot, identity.DeviceId, e.EntityId)));

        // Every single-component path this identity ever owned, whether or not the record says it was
        // dealt with: a removal is final, so it does not depend on the record being complete.
        topics.AddRange(retired.Select(r => DiscoveryTopics.Component(
            identity.DiscoveryPrefix, r.Component, identity.DeviceId, r.EntityId)));
        topics.AddRange(migrating.Select(m => DiscoveryTopics.Component(
            identity.DiscoveryPrefix, m.Component, identity.DeviceId, m.EntityId)));

        if (record is not null) next.Devices.Remove(record);

        return ([.. Distinct(topics).Select(MqttMessage.Empty)], next);
    }

    /// <summary>Everything one recorded identity owns, emptied: the document that created the device,
    /// the availability topics its will and its withheld components retained, and every state topic it
    /// published on.</summary>
    private static IEnumerable<MqttMessage> Abandon(PublishedDevice device)
    {
        yield return MqttMessage.Empty(device.ConfigTopic);
        if (device.AvailabilityTopic.Length > 0) yield return MqttMessage.Empty(device.AvailabilityTopic);
        if (device.WithheldTopic.Length > 0) yield return MqttMessage.Empty(device.WithheldTopic);
        foreach (var entity in device.Entities.Where(e => e.StateTopic.Length > 0))
            yield return MqttMessage.Empty(entity.StateTopic);
    }

    /// <summary>The retirements this identity has not yet made, and the record of having made them.</summary>
    private static void Retire(
        PublishedDevice current,
        MqttDeviceIdentity identity,
        IReadOnlyList<RetiredEntity> retired,
        List<MqttMessage> evictions)
    {
        foreach (var entry in retired)
        {
            string topic = DiscoveryTopics.Component(
                identity.DiscoveryPrefix, entry.Component, identity.DeviceId, entry.EntityId);

            // Keyed on the composed topic, component segment and all: that is the thing being emptied,
            // and an id on its own would make one component's retirement look like another's.
            // A migrated topic is never retired — the two mean opposite things at one address.
            if (current.Retired.Contains(topic) || current.Migrated.Contains(topic)) continue;

            current.Retired.Add(topic);
            evictions.Add(MqttMessage.Empty(topic));
        }
    }

    /// <summary>The handovers this identity has not yet made: the flag before the document, and the
    /// cleanup after it.</summary>
    private static void Migrate(
        PublishedDevice current,
        MqttDeviceIdentity identity,
        IReadOnlyList<MigratingEntity> migrating,
        List<MqttMessage> evictions,
        List<MqttMessage> sweep)
    {
        foreach (var entry in migrating)
        {
            string topic = DiscoveryTopics.Component(
                identity.DiscoveryPrefix, entry.Component, identity.DeviceId, entry.EntityId);
            if (current.Migrated.Contains(topic)) continue;

            current.Migrated.Add(topic);
            evictions.Add(new MqttMessage(topic, DiscoveryTopics.MigratePayload, Retain: true));
            sweep.Add(MqttMessage.Empty(topic));
        }
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> topics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string topic in topics)
            if (topic.Length > 0 && seen.Add(topic)) yield return topic;
    }

    private static PublishedEntity Record(string topicRoot, string deviceId, MqttEntity entity) => new()
    {
        EntityId = entity.EntityId,
        Platform = entity.Platform,
        // Only a retained topic holds anything to evict.
        StateTopic = entity is { HasState: true, Retain: true }
            ? MqttTopics.Channel(topicRoot, deviceId, entity.EntityId)
            : "",
    };
}
