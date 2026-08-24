namespace ZeroZero.Mqtt;

/// <summary>The identity in force for one apply round: the device id every topic is composed from,
/// plus the two values the connection carries but never reads. Swapped as a whole record, so a
/// reader never pairs one round's device id with another's prefix.</summary>
public sealed record MqttDeviceIdentity(string DeviceId, string DiscoveryPrefix, string DeviceName);

/// <summary>What runs alongside the connection at the three points a layer above it has to act: on
/// connect, when the device is removed, and when an identity is superseded. The connection knows
/// nothing of what the listener does — this is how the discovery layer sits on top without the core
/// depending on it.</summary>
/// <remarks>There is deliberately no callback for stopping. Stopping publishes offline and leaves
/// everything else exactly as it is, so there is nothing for a layer above to do; removal is the
/// separate, explicit operation below.</remarks>
public interface IMqttConnectionListener
{
    /// <summary>Runs on every (re)connect, before availability is published, so nothing is announced
    /// as online before the thing being announced exists.</summary>
    Task OnConnectedAsync(IMqttPublisher publisher, MqttDeviceIdentity identity, CancellationToken ct);

    /// <summary>Runs when the device is being removed outright, before the connection empties its own
    /// retained topics.</summary>
    /// <remarks>Reached only from <see cref="MqttConnection.RemoveDeviceAsync"/>. Never from switching
    /// publishing off, and never from a configuration that has stopped being complete: this deletes
    /// the receiver's registry entries, and with them every name, entity id and area the user chose,
    /// so it happens because somebody asked for it and for no other reason.</remarks>
    Task OnRemovingAsync(IMqttPublisher publisher, MqttDeviceIdentity identity, CancellationToken ct);

    /// <summary>Runs for an identity that has been superseded, so nothing it owned is left retained.</summary>
    Task OnIdentityRetiredAsync(IMqttPublisher publisher, MqttDeviceIdentity retired, CancellationToken ct);
}

/// <summary>Everything a connection needs that does not vary with broker settings: the topic root,
/// what is published, what may be commanded, the availability payloads, the endpoint-memory
/// callbacks and the log sink.</summary>
public sealed record MqttConnectionSetup
{
    /// <summary>The application's own segment at the head of every topic. The identity that keeps
    /// every topic and every <c>unique_id</c> stable while one implementation serves many
    /// applications.</summary>
    public required string TopicRoot { get; init; }

    /// <summary>The retained topics this application publishes on — one per entity, each carrying a
    /// plain value. Replaceable at runtime through <see cref="MqttConnection.SetChannelsAsync"/>.</summary>
    public IReadOnlyList<MqttChannel> Channels { get; init; } = [];

    /// <summary>The command entities. One wildcard subscription covers them all; the router resolves
    /// by entity id.</summary>
    public IReadOnlyList<MqttCommandTarget> CommandTargets { get; init; } = [];

    /// <summary>Subscriptions outside the command tree, with their handlers. Subscribed on every
    /// connect alongside the command wildcard.</summary>
    public IReadOnlyList<MqttSubscription> Subscriptions { get; init; } = [];

    /// <summary>The device name used when the connect parameters carry none. Takes the machine name.</summary>
    public Func<string, string> DefaultDeviceName { get; init; } = machine => machine;

    /// <summary>The Last Will payload and its complement. Published retained at the availability
    /// topic: the will on an ungraceful drop, the second on connect.</summary>
    public string OnlinePayload { get; init; } = "online";

    public string OfflinePayload { get; init; } = "offline";

    /// <summary>Where the broker last answered, for the sweep to lead with. Read once per apply.</summary>
    /// <remarks>A pair of callbacks rather than a field on the settings record. Endpoint memory is
    /// state the connection discovers, and persisting it as a setting would make a successful connect
    /// a settings change — which a consumer that re-applies on a settings change turns into a
    /// reconnect on the strength of its own success.</remarks>
    public Func<MqttEndpointMemory?>? RecallEndpoint { get; init; }

    /// <summary>Called when the broker answers somewhere new, so the host can persist it wherever it
    /// keeps state. Null keeps the memory for the life of the process and no longer.</summary>
    public Action<MqttEndpointMemory>? RememberEndpoint { get; init; }

    /// <summary>Where a command that was not acted on is reported. The module supplies the facts and
    /// the entity's own <see cref="MqttCommandVerdict.Detail"/>; it composes no sentence of its own,
    /// because only the application knows why a value it understands is one it will not act on.</summary>
    public Action<MqttCommandRefusal>? CommandRefused { get; init; }

    public IMqttLog Log { get; init; } = NullMqttLog.Instance;

    /// <summary>The layer above, or null for a connection that publishes without announcing.</summary>
    public IMqttConnectionListener? Listener { get; init; }
}
