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

    /// <summary>What the receiver shows. Free text, and no id is derived from it. Null makes the
    /// entity the device's main feature: the receiver then names it after the device alone instead of
    /// "Device Entity".</summary>
    /// <remarks>Required rather than defaulted, because "the main feature" is a declaration and there
    /// can be only one of it per device.</remarks>
    public required string? Name { get; init; }

    /// <summary>The publish-group key, or null for an entity that is always published.</summary>
    public string? Group { get; init; }

    public MqttEntityCategory Category { get; init; }

    /// <summary>An icon name in the receiver's own vocabulary, or null to leave it the choice.</summary>
    public string? Icon { get; init; }

    /// <summary>The receiver's device class, which decides the icon, the unit handling and the
    /// wording it uses. An open vocabulary, so a string rather than an enum that would go stale.</summary>
    public string? DeviceClass { get; init; }

    /// <summary>Capability gating, evaluated on every announcement pass. Null means true.</summary>
    /// <remarks>
    /// <para>One predicate decides membership and capability together with the group: an entity is
    /// published when its group is on <b>and</b> this returns true. That is what keeps a select with
    /// nothing to offer, or a control the hardware does not expose, withheld rather than announced
    /// empty.</para>
    /// <para>It runs on the announcement thread and usually reads live hardware, so it can fail as
    /// well as answer. A throw is <b>not</b> a false: it says the capability could not be read, not
    /// that it is absent, and the two must not be the same announcement. A controller that does not
    /// answer, a management interface that times out, a busy resource — all of them look like "absent"
    /// to a predicate that can only return a boolean, and a reconnect after resume from standby is
    /// exactly when they are least likely to answer. So a throw keeps whatever the record already says
    /// about the entity, and one unanswered read cannot rewrite the document.</para>
    /// </remarks>
    public Func<bool>? Include { get; init; }

    /// <summary>How long a requested publish waits before reading the entity, so a burst of signals
    /// collapses into one read and an in-progress write lands before the read that reports it.</summary>
    public TimeSpan Debounce { get; init; }

    /// <summary>Whether the receiver enables the entity when it first appears. False for something
    /// worth publishing but not worth showing until someone asks for it.</summary>
    public bool EnabledByDefault { get; init; } = true;

    /// <summary>Whether the state topic is published retained, so a receiver connecting later has a
    /// value at once. False for a reading that expires: the broker replays a retained payload on every
    /// subscribe, and an expiry that already elapsed comes back looking current.</summary>
    /// <remarks>A non-retained state topic holds nothing, so nothing is recorded against it and there
    /// is nothing to empty when the entity goes.</remarks>
    public bool Retain { get; init; } = true;

    /// <summary>Whether an absent reading leaves the last payload published standing instead of the
    /// absent-reading sentinel, and is sent again on a (re)connect. For an entity whose producer has a
    /// first reading to wait for — a poll that has not run, hardware that has not answered yet — where
    /// the sentinel would otherwise show as a visible unknown before the first real value.</summary>
    /// <remarks>
    /// <para>It holds on every pass, not only on a connect: an entity that loses its reading keeps the
    /// value it last published rather than clearing to <see cref="NoValuePayload"/>. Whatever stands
    /// is the last reading taken, of no stated age.</para>
    /// <para>An entity that has never had a reading publishes nothing at all rather than the sentinel,
    /// so the receiver shows it unknown until the first real value arrives.</para>
    /// </remarks>
    public bool RepublishLastOnConnect { get; init; }

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
    /// <para>Every platform that ignores a zero-length payload — sensor, binary sensor, switch, number
    /// and select — publishes <see cref="MqttPayload.None"/>, because emptying the topic there leaves
    /// the last value standing, which is the stale state the rule exists to prevent. Only
    /// <see cref="MqttText"/> keeps null: an empty string is a value there.</para>
    /// <para>The behaviour is per platform rather than universal, so each component type states its
    /// own and a correction is one line in one class.</para>
    /// </remarks>
    public abstract string? NoValuePayload { get; }

    /// <summary>Whether this entity's topic must never carry an empty payload.</summary>
    public bool AlwaysCarriesValue => NoValuePayload is not null;

    /// <summary>The payload to publish now, or null to empty the topic. Null from an entity that
    /// always carries a value is impossible: its sentinel stands in.</summary>
    public string? ReadState() => HasState ? ReadPayload() ?? NoValuePayload : null;

    /// <summary>The payload to publish now, with nothing standing in for an absent reading. What a
    /// channel declaring <see cref="RepublishLastOnConnect"/> reads, because the sentinel would
    /// otherwise answer the absent reading before the channel could.</summary>
    internal string? ReadStateWithoutSentinel() => HasState ? ReadPayload() : null;

    /// <summary>Whether a given configuration publishes this entity: true to publish, false to
    /// withhold, and null when <see cref="Include"/> could not answer.</summary>
    /// <param name="groups">The group state as it stood at the start of the pass, or null for a
    /// consumer that declares no groups.</param>
    /// <remarks>A switched-off group is a decision the user made and answers false outright, without
    /// the capability being read at all — there is nothing to publish either way, and reading hardware
    /// for an entity nobody wants is work for its own sake.</remarks>
    public bool? IsPublished(PublishGroupSnapshot? groups)
    {
        if (!(groups?.IsEnabled(Group) ?? true)) return false;
        if (Include is not { } include) return true;

        try { return include(); }
        catch { return null; }
    }

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
