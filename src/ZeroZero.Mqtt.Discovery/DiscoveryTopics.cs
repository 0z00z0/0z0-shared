namespace ZeroZero.Mqtt.Discovery;

/// <summary>One <c>(Component, EntityId)</c> pair that no longer exists: its retained per-component
/// config is emptied, which removes the receiver's entry for it.</summary>
/// <remarks>
/// <para>This is a consumer's own renaming history: an entity that once published under a different
/// id, or under a different component, left a retained config at a path nothing composes any more.
/// Nothing in the ledger can help — the ledger only knows what this installation published — so the
/// list is declared in source and kept indefinitely.</para>
/// <para>The component segment is what keeps the entry apart from a live entity of the same id. The
/// receiver scopes discovery identity by component, and so does the topic, so retiring
/// <c>binary_sensor/x</c> while <c>switch/x</c> is published touches nothing the live entity owns.
/// Only the identical pair collides, and that is refused at declaration. Nothing else in the
/// publisher protects this: every topic it empties is composed with its component, and the ledger
/// records composed topics rather than ids for the same reason.</para>
/// <para>Emptied once per identity and then written down, not on every connect: repeating it on every
/// reconnect, resume and receiver restart spends a publish each time and re-runs a removal against a
/// path a consumer may since have started using again.</para>
/// <para>Removing an entry is silent and permanent: an installation upgrading from before the entity
/// was withdrawn keeps a ghost with nothing left to evict it.</para>
/// </remarks>
public readonly record struct RetiredEntity(string Component, string EntityId);

/// <summary>One <c>(Component, EntityId)</c> pair moving from its own single-component config into the
/// device document.</summary>
/// <remarks>
/// The documented handover, published for conformance rather than as a repair: the flag unloads the
/// single-component item while keeping its registry entry, the device document takes the entity over,
/// and the old topic is emptied last. Order is the whole of it — the new announcement exists before
/// the old one is removed — and it is the sequence a receiver documents, whatever a given version
/// happens to tolerate.
/// <para>Published with no version gate. Device-based discovery and the flag shipped in one change, so
/// a receiver able to read the document already honours it, and one older than that never subscribes
/// to the device topic at all.</para>
/// <para>Recorded in the ledger separately from a retirement. The two write one topic with opposite
/// intent — hand over, versus remove — so a restart must never replay one as the other. An entity may
/// not be declared both.</para>
/// </remarks>
public readonly record struct MigratingEntity(string Component, string EntityId);

/// <summary>One retained value topic that nothing publishes on any more: it is emptied, so no stale
/// payload is left standing where no declaration reaches.</summary>
/// <remarks>
/// <para>The key is the topic segment below <c>&lt;topicRoot&gt;/&lt;deviceId&gt;/</c> — the same
/// shape as <see cref="MqttChannel.Key"/> and as an entity id — and the topic is composed by
/// <see cref="MqttTopics.Channel"/>.</para>
/// <para>What separates this from <see cref="RetiredEntity"/> is what it reaches: a value topic, not a
/// discovery config. It is the declaration for a consumer moving off a hand-rolled or shared-payload
/// predecessor, whose retained payloads the ledger has never heard of and no entity declaration
/// composes.</para>
/// <para>It names a key under this application's own topic root and device id. A predecessor that
/// published under a different root is a different identity and is out of reach here.</para>
/// <para>Emptied once per identity and then written down, exactly as a retirement is: repeating it on
/// every reconnect, resume and receiver restart spends a publish each time and re-runs a removal
/// against a key a consumer may since have started using again.</para>
/// <para>Removing an entry is silent and permanent, as with <see cref="RetiredEntity"/>: an
/// installation upgrading from before the entry was withdrawn keeps a retained payload with nothing
/// left to empty it.</para>
/// </remarks>
public readonly record struct RetiredChannel(string Key);

/// <summary>Whether a set of declarations can be published together. Pure.</summary>
public static class DiscoveryDeclaration
{
    /// <summary>Why the declarations contradict each other, or null when they do not.</summary>
    /// <remarks>An entity id alone is not a collision: the config topic and the receiver's own
    /// registry key both carry the component, so the same id under two components is two entities.
    /// What is refused is the identical pair, where one declaration would empty the very topic the
    /// other publishes.</remarks>
    public static string? Validate(
        MqttEntitySet entities,
        IReadOnlyList<RetiredEntity> retired,
        IReadOnlyList<MigratingEntity> migrating,
        IReadOnlyList<RetiredChannel>? retiredChannels = null)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (var entry in retired)
        {
            // Same component and same id is one config topic with two owners. The eviction lands
            // before the document, so the receiver deletes and immediately recreates on every pass
            // that runs it — invisible from here, and it loses the user's chosen entity id outright if
            // anything claims that id in the gap.
            if (entities.Find(entry.EntityId) is { } live
                && string.Equals(live.Platform, entry.Component, StringComparison.Ordinal))
                return $"Retired entity '{entry.Component}/{entry.EntityId}' is also a live entity, and "
                     + "retiring it would empty the config the live entity is published at.";
        }

        var retiredPairs = retired
            .Select(r => $"{r.Component}/{r.EntityId}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in migrating)
        {
            if (retiredPairs.Contains($"{entry.Component}/{entry.EntityId}"))
                return $"Entity '{entry.Component}/{entry.EntityId}' is declared both retired and "
                     + "migrating, and the two intents cannot both be honoured.";
        }

        IReadOnlyList<RetiredChannel> channels = retiredChannels ?? [];
        foreach (var entry in channels)
        {
            if (MqttTopics.ValidateChannelKey(entry.Key) is { } unusable) return unusable;

            // A live entity with state publishes on exactly this topic. There is no component segment
            // here to keep the two apart, so the key alone decides it: only an entity that publishes
            // nothing — a button — leaves the topic free.
            if (entities.Find(entry.Key) is { HasState: true })
                return $"Retired channel '{entry.Key}' is also a live entity, and retiring it would "
                     + "empty the topic the live entity publishes on.";
        }

        return null;
    }
}

/// <summary>Where the discovery layer's topics live. Pure.</summary>
public static class DiscoveryTopics
{
    /// <summary>The segment that marks a whole-device document, as against a single component's.</summary>
    public const string DeviceSegment = "device";

    /// <summary>What is published to a single-component config topic to hand the item over to a device
    /// document rather than remove it.</summary>
    public const string MigratePayload = """{"migrate_discovery":true}""";

    /// <summary>The device document, at <c>&lt;prefix&gt;/device/&lt;deviceId&gt;/config</c>. One
    /// retained payload describing every component the device publishes.</summary>
    public static string Device(string prefix, string deviceId) =>
        $"{prefix}/{DeviceSegment}/{deviceId}/config";

    /// <summary>The device id a recorded document topic was published under, or null when the topic is
    /// not one of these. What lets a record written before the id was stored alongside the topic be
    /// read without losing the identity it carries.</summary>
    public static string? DeviceIdOf(string? configTopic)
    {
        const string tail = "/config";
        if (configTopic is null || !configTopic.EndsWith(tail, StringComparison.Ordinal)) return null;

        var body = configTopic.AsSpan(0, configTopic.Length - tail.Length);
        int slash = body.LastIndexOf('/');
        if (slash <= 0 || body.Length <= slash + 1) return null;

        var head = body[..slash];
        return head.Equals(DeviceSegment, StringComparison.Ordinal)
            || head.EndsWith($"/{DeviceSegment}", StringComparison.Ordinal)
            ? body[(slash + 1)..].ToString()
            : null;
    }

    /// <summary>One component's own retained config, at
    /// <c>&lt;prefix&gt;/&lt;component&gt;/&lt;deviceId&gt;/&lt;entityId&gt;/config</c>. The layer
    /// publishes no configuration here; it empties these paths for the entities a consumer declares
    /// <see cref="RetiredEntity"/>, and hands over the ones it declares
    /// <see cref="MigratingEntity"/>.</summary>
    /// <remarks>The component segment is load-bearing. It is what keeps a retirement of one component's
    /// entity off another component's live config, and it is why every topic this layer empties is
    /// composed here rather than assembled from an id.</remarks>
    public static string Component(string prefix, string component, string deviceId, string entityId) =>
        $"{prefix}/{component}/{deviceId}/{entityId}/config";

    /// <summary>The birth topic a receiver publishes on when it restarts.</summary>
    public static string Status(string prefix) => $"{prefix}/status";
}
