namespace ZeroZero.Mqtt.Discovery;

/// <summary>What one announcement pass puts on the wire, in the order it has to happen.</summary>
/// <param name="Evictions">Sent before the document: identities this installation no longer
/// publishes under, and the retired per-component configs. Emptying a superseded identity first is
/// what stops a receiver holding two devices at once.</param>
/// <param name="Document">The retained device document.</param>
/// <param name="Sweep">Sent after the document: the state topics of entities the document has just
/// removed. After, because a value outliving its component for a moment is harmless and a component
/// arriving before its own removal is not.</param>
/// <param name="Ledger">What the record becomes once the pass has landed.</param>
internal sealed record DiscoveryPass(
    IReadOnlyList<MqttMessage> Evictions,
    string ConfigTopic,
    string Document,
    IReadOnlyList<MqttMessage> Sweep,
    DiscoveryLedger Ledger);

/// <summary>Reconciles what was published against what is to be published. Pure: the ledger and the
/// entity list go in, the messages and the next ledger come out.</summary>
internal static class DiscoveryPlan
{
    /// <summary>The pass that announces <paramref name="published"/> and evicts everything the record
    /// names that is not in it.</summary>
    /// <param name="includeRetired">Whether the declared <see cref="RetiredEntity"/> configs are
    /// emptied. True on a connect, where they cost one publish each; false on a republish within a
    /// session, where nothing can have retained them again.</param>
    public static DiscoveryPass Announce(
        DiscoveryLedger ledger,
        string topicRoot,
        MqttDeviceIdentity identity,
        DiscoveryDevice device,
        DiscoveryOrigin origin,
        IReadOnlyList<MqttEntity> published,
        IReadOnlyList<RetiredEntity> retired,
        string onlinePayload,
        string offlinePayload,
        bool includeRetired)
    {
        string configTopic = DiscoveryTopics.Device(identity.DiscoveryPrefix, identity.DeviceId);
        var next = ledger.Copy();

        var evictions = new List<MqttMessage>();
        foreach (var abandoned in next.Devices.Where(d => d.ConfigTopic != configTopic).ToList())
        {
            evictions.AddRange(Abandon(abandoned));
            next.Devices.Remove(abandoned);
        }

        if (includeRetired)
            evictions.AddRange(retired.Select(r => MqttMessage.Empty(
                DiscoveryTopics.Component(
                    identity.DiscoveryPrefix, r.Component, identity.DeviceId, r.EntityId))));

        var current = next.Find(configTopic);
        var wanted = published.Select(e => Record(topicRoot, identity.DeviceId, e)).ToList();
        var keep = wanted.Select(e => e.EntityId).ToHashSet(StringComparer.Ordinal);

        // Everything the record names and this configuration does not publish: a group switched off,
        // an Include gone false, an entity dropped from the set — and, because the record outlives the
        // process, an entity dropped while the application was closed.
        var gone = current?.Entities.Where(e => !keep.Contains(e.EntityId)).ToList() ?? [];

        if (current is null) next.Devices.Add(current = new PublishedDevice { ConfigTopic = configTopic });
        current.AvailabilityTopic = MqttTopics.Availability(topicRoot, identity.DeviceId);
        current.Entities = wanted;

        return new DiscoveryPass(
            evictions,
            configTopic,
            DiscoveryDocument.Build(
                topicRoot, identity, device, origin, published, gone, onlinePayload, offlinePayload),
            [.. gone.Where(e => e.StateTopic.Length > 0).Select(e => MqttMessage.Empty(e.StateTopic))],
            next);
    }

    /// <summary>The pass that removes one identity outright: the whole device goes, and everything
    /// the record says it retained goes with it. What switching publishing off and superseding a
    /// device id both come through.</summary>
    public static (IReadOnlyList<MqttMessage> Messages, DiscoveryLedger Ledger) Withdraw(
        DiscoveryLedger ledger,
        string discoveryPrefix,
        string deviceId,
        IReadOnlyList<RetiredEntity> retired)
    {
        string configTopic = DiscoveryTopics.Device(discoveryPrefix, deviceId);
        var next = ledger.Copy();

        var messages = new List<MqttMessage>();
        if (next.Find(configTopic) is { } record)
        {
            messages.AddRange(Abandon(record));
            next.Devices.Remove(record);
        }
        else
        {
            // Nothing was recorded under this identity — a first run, or a ledger that was lost.
            // The device document is still emptied: a zero-length retained payload there costs one
            // publish and removes whatever an earlier installation left.
            messages.Add(MqttMessage.Empty(configTopic));
        }

        messages.AddRange(retired.Select(r => MqttMessage.Empty(
            DiscoveryTopics.Component(discoveryPrefix, r.Component, deviceId, r.EntityId))));

        return (messages, next);
    }

    /// <summary>Everything one recorded identity owns, emptied: the document that created the device,
    /// the availability topic its will retained, and every state topic it published on.</summary>
    private static IEnumerable<MqttMessage> Abandon(PublishedDevice device)
    {
        yield return MqttMessage.Empty(device.ConfigTopic);
        if (device.AvailabilityTopic.Length > 0) yield return MqttMessage.Empty(device.AvailabilityTopic);
        foreach (var entity in device.Entities.Where(e => e.StateTopic.Length > 0))
            yield return MqttMessage.Empty(entity.StateTopic);
    }

    private static PublishedEntity Record(string topicRoot, string deviceId, MqttEntity entity) => new()
    {
        EntityId = entity.EntityId,
        Platform = entity.Platform,
        StateTopic = entity.HasState ? MqttTopics.Channel(topicRoot, deviceId, entity.EntityId) : "",
    };
}
