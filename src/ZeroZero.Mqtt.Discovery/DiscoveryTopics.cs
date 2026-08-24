namespace ZeroZero.Mqtt.Discovery;

/// <summary>One <c>(Component, EntityId)</c> pair whose retained per-component config must go on
/// being emptied. Declared by the application, emptied by the publisher on every connect.</summary>
/// <remarks>
/// This is a consumer's own renaming history: an entity that once published under a different id, or
/// under a different component, left a retained config at a path nothing composes any more. Nothing
/// in the ledger can help — the ledger only knows what this installation published — so the list is
/// declared in source and kept indefinitely. Each entry costs one publish per connect and a few
/// strings; removing one is silent and permanent, because an installation upgrading from before the
/// entity was withdrawn keeps a ghost with nothing left to evict it.
/// </remarks>
public readonly record struct RetiredEntity(string Component, string EntityId);

/// <summary>Where the discovery layer's topics live. Pure.</summary>
public static class DiscoveryTopics
{
    /// <summary>The segment that marks a whole-device document, as against a single component's.</summary>
    public const string DeviceSegment = "device";

    /// <summary>The device document, at <c>&lt;prefix&gt;/device/&lt;deviceId&gt;/config</c>. One
    /// retained payload describing every component the device publishes.</summary>
    public static string Device(string prefix, string deviceId) =>
        $"{prefix}/{DeviceSegment}/{deviceId}/config";

    /// <summary>One component's own retained config, at
    /// <c>&lt;prefix&gt;/&lt;component&gt;/&lt;deviceId&gt;/&lt;entityId&gt;/config</c>. The layer
    /// publishes nothing here; it empties these paths for the entities a consumer declares
    /// <see cref="RetiredEntity"/>.</summary>
    public static string Component(string prefix, string component, string deviceId, string entityId) =>
        $"{prefix}/{component}/{deviceId}/{entityId}/config";

    /// <summary>The birth topic a receiver publishes on when it restarts.</summary>
    public static string Status(string prefix) => $"{prefix}/status";
}
