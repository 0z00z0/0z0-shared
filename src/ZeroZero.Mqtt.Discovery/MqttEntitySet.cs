namespace ZeroZero.Mqtt.Discovery;

/// <summary>The full published surface, and the pure decision of which part of it a given
/// configuration announces.</summary>
/// <remarks>
/// Nothing here touches an MQTT client or a settings singleton: a group snapshot goes in and the
/// answer comes out, so the same call answers "what to publish", "what to route" and "what to evict".
/// Immutable — a set that changes at runtime is replaced, not mutated, so a pass reading it cannot
/// see half of a change.
/// </remarks>
public sealed class MqttEntitySet
{
    /// <summary>An empty set, for a consumer whose entities are not known until something has run.</summary>
    public static readonly MqttEntitySet Empty = new([]);

    private readonly MqttEntity[] _all;
    private readonly Dictionary<string, MqttEntity> _byId;

    /// <summary>Rejects a duplicate entity id, and an id that is not topic-safe.</summary>
    /// <exception cref="ArgumentException">Two entities share an id, or an id would not survive being
    /// put in a topic. A shared id means one <c>unique_id</c>, so the second entity would replace the
    /// first in the receiver's registry and take the first's commands with it — a correctness failure,
    /// and an easy one to reach when ids are composed from names the machine supplies.</exception>
    public MqttEntitySet(IEnumerable<MqttEntity> entities)
    {
        _all = [.. entities];
        _byId = new Dictionary<string, MqttEntity>(_all.Length, StringComparer.Ordinal);

        foreach (var entity in _all)
        {
            if (entity.Validate() is { } error)
                throw new ArgumentException(error, nameof(entities));
            if (!_byId.TryAdd(entity.EntityId, entity))
                throw new ArgumentException(
                    $"Two entities share the id '{entity.EntityId}'.", nameof(entities));
        }
    }

    /// <summary>Every entity the application knows how to publish, announced or not.</summary>
    public IReadOnlyList<MqttEntity> All => _all;

    public MqttEntity? Find(string entityId) => _byId.GetValueOrDefault(entityId);

    /// <summary>The name a user would recognise, for a status line that has only an entity id.</summary>
    public string NameOf(string entityId) => Find(entityId)?.Name ?? entityId;

    /// <summary>The entities a given configuration announces.</summary>
    public IReadOnlyList<MqttEntity> Published(PublishGroupSnapshot? groups) =>
        [.. _all.Where(e => e.IsPublished(groups))];

    /// <summary>The complement of <see cref="Published"/>: switched off, or gated out by
    /// <see cref="MqttEntity.Include"/>.</summary>
    public IReadOnlyList<MqttEntity> Withheld(PublishGroupSnapshot? groups) =>
        [.. _all.Where(e => !e.IsPublished(groups))];

    /// <summary>The retained state topics the connection publishes on, one per announced entity that
    /// has state. A button contributes none.</summary>
    public static IReadOnlyList<MqttChannel> Channels(IReadOnlyList<MqttEntity> published) =>
        [.. published.Where(e => e.HasState).Select(Channel)];

    /// <summary>The command targets the router resolves against, one per announced entity that takes
    /// commands. An entity that is not announced routes nowhere, so a command addressed to a
    /// switched-off group is reported as unrecognised rather than quietly acted on.</summary>
    public static IReadOnlyList<MqttCommandTarget> CommandTargets(IReadOnlyList<MqttEntity> published) =>
        [.. published.OfType<MqttCommandEntity>().Select(e => new MqttCommandTarget(e.EntityId, e.Accept))];

    private static MqttChannel Channel(MqttEntity entity) =>
        new(entity.EntityId, entity.ReadState, Retain: true, Debounce: entity.Debounce);
}
