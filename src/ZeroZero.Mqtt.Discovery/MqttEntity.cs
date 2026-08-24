namespace ZeroZero.Mqtt.Discovery;

/// <summary>How an entity is filed in the receiver's interface. <see cref="Primary"/> writes no
/// <c>entity_category</c>, which is what keeps a primary control on the main card rather than behind
/// a fold.</summary>
public enum MqttEntityCategory { Primary, Config, Diagnostic }

/// <summary>One published thing. Abstract, because the component type decides the platform name, the
/// discovery keys, whether there is a state topic at all, and what an absent reading puts on the
/// wire.</summary>
/// <remarks>
/// <para>The hierarchy is closed: the seven component types are the receiver's whole vocabulary, so
/// an eighth is a change here rather than in a consumer, and every consumer gets the new keys with
/// it.</para>
/// <para>An entity owns its own reader, typed to what the platform carries. Nothing composes a JSON
/// payload and nothing writes a <c>value_template</c>: one bare topic per entity, a plain value, and
/// a shell script or a flow engine reads it with no parsing.</para>
/// </remarks>
public abstract class MqttEntity
{
    // Closes the hierarchy to this assembly. A subclass elsewhere would have to invent a platform
    // name the receiver does not know, and would compose its keys with no way to be tested here.
    private protected MqttEntity() { }

    /// <summary>The <c>unique_id</c> stem, the state topic's last segment and the command topic's.
    /// Stable for the life of the entity: it is what carries the entity across any change to its
    /// topics or to the discovery format.</summary>
    /// <remarks>Must already be in <see cref="MqttEntityId"/>'s alphabet. An id composed from a
    /// runtime name goes through <see cref="MqttEntityIdAllocator"/> first, which also resolves the
    /// collisions such names produce.</remarks>
    public required string EntityId { get; init; }

    /// <summary>What the receiver shows. Free text, and no id is derived from it.</summary>
    public required string Name { get; init; }

    /// <summary>The publish-group key, or null for an entity that is always published.</summary>
    public string? Group { get; init; }

    public MqttEntityCategory Category { get; init; }

    /// <summary>An icon name in the receiver's own vocabulary, or null to leave it the choice.</summary>
    public string? Icon { get; init; }

    /// <summary>The receiver's device class, which decides the icon, the unit handling and the
    /// wording it uses. An open vocabulary, so a string rather than an enum that would go stale.</summary>
    public string? DeviceClass { get; init; }

    /// <summary>Capability gating, evaluated on every announcement pass. Null means true.</summary>
    /// <remarks>One predicate decides membership and capability together with the group: an entity is
    /// published when its group is on <b>and</b> this returns true. That is what keeps a select with
    /// nothing to offer, or a control the hardware does not expose, omitted rather than announced
    /// empty.</remarks>
    public Func<bool>? Include { get; init; }

    /// <summary>How long a requested publish waits before reading the entity, so a burst of signals
    /// collapses into one read and an in-progress write lands before the read that reports it.</summary>
    public TimeSpan Debounce { get; init; }

    /// <summary>Discovery keys this model has no property for. Merged into the component entry last,
    /// so an entry here wins.</summary>
    /// <remarks>The escape hatch, and deliberately small: a key that turns out to be worth declaring
    /// becomes a typed property on the component that owns it.</remarks>
    public IReadOnlyDictionary<string, object?>? Extra { get; init; }

    /// <summary>The receiver's platform name — the <c>p</c> key inside the component entry.</summary>
    public abstract string Platform { get; }

    /// <summary>Whether the entity has a state topic. False for a button, which is command-only: it
    /// declares no state channel and publishes no payload.</summary>
    public virtual bool HasState => true;

    /// <summary>Whether the entity has a command topic.</summary>
    public bool IsCommand => this is MqttCommandEntity;

    /// <summary>What is published when the entity has no current reading.</summary>
    /// <remarks>
    /// Null empties the topic, which is how a receiver is told the value is unknown. A platform that
    /// ignores an empty payload — and so goes on showing the last value it saw — declares a sentinel
    /// instead. The behaviour is per platform rather than universal, so each component type states
    /// its own and a correction is one line in one class.
    /// </remarks>
    public abstract string? NoValuePayload { get; }

    /// <summary>Whether this entity's topic must never carry an empty payload.</summary>
    public bool AlwaysCarriesValue => NoValuePayload is not null;

    /// <summary>The payload to publish now, or null to empty the topic. Null from an entity that
    /// always carries a value is impossible: its sentinel stands in.</summary>
    public string? ReadState() => HasState ? ReadPayload() ?? NoValuePayload : null;

    /// <summary>Whether a given configuration publishes this entity.</summary>
    /// <param name="groups">The group state as it stood at the start of the pass, or null for a
    /// consumer that declares no groups.</param>
    public bool IsPublished(PublishGroupSnapshot? groups) =>
        (groups?.IsEnabled(Group) ?? true) && (Include?.Invoke() ?? true);

    /// <summary>Why this entity cannot be published, or null when it can.</summary>
    internal virtual string? Validate() =>
        MqttEntityId.Normalise(EntityId) != EntityId
            ? $"Entity id '{EntityId}' is not in the topic-safe alphabet. Compose it through {nameof(MqttEntityIdAllocator)}."
            : MqttTopics.ValidateChannelKey(EntityId);

    /// <summary>The keys specific to this component, written after the shared ones.</summary>
    internal abstract void Describe(DiscoveryKeys keys);

    /// <summary>The current reading, or null when there is none.</summary>
    private protected abstract string? ReadPayload();
}

/// <summary>An entity the receiver can write to. Adds the one member the domain seam is made of:
/// parse the payload, validate it against the application's own bounds, and return either a refusal
/// carrying a reason or the work to run.</summary>
/// <remarks>A refusal publishes nothing, changes nothing and clamps nothing. The component parses as
/// far as its own type goes — a number to a double, a switch to a boolean — and hands the typed value
/// on, so no consumer parses a payload twice.</remarks>
public abstract class MqttCommandEntity : MqttEntity
{
    /// <summary>Judges one inbound payload. Runs on the receive callback, so it decides and returns;
    /// the work it carries is run on the command worker.</summary>
    public abstract MqttCommandVerdict Accept(string payload);
}
