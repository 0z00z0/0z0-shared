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

    /// <summary>The name a user would recognise, for a status line that has only an entity id. Falls
    /// back to the id, which is also what a main-feature entity — one that declares no name of its
    /// own — has to be called in a line about one entity rather than about the device.</summary>
    public string NameOf(string entityId) => Find(entityId)?.Name ?? entityId;

    /// <summary>The entities a given configuration announces. An entity whose capability could not be
    /// read is not one of them — use <see cref="Resolve"/> where the record can say what it was.</summary>
    public IReadOnlyList<MqttEntity> Published(PublishGroupSnapshot? groups) =>
        [.. _all.Where(e => e.IsPublished(groups) == true)];

    /// <summary>The complement of <see cref="Published"/>: switched off, or gated out by
    /// <see cref="MqttEntity.Include"/>.</summary>
    /// <remarks>A reversible state, and the announcement pass needs it as an input: an entity here has
    /// stopped publishing but has not stopped existing, so one already announced stays in the document
    /// and is shown unavailable rather than removed. Without it, a group toggle and a deletion are the
    /// same thing to a receiver — and a group toggle is a settings checkbox that commits at once.</remarks>
    public IReadOnlyList<MqttEntity> Withheld(PublishGroupSnapshot? groups) =>
        [.. _all.Where(e => e.IsPublished(groups) != true)];

    /// <summary>The two lists one announcement pass works from, with an unreadable capability resolved
    /// against what was published last time rather than guessed.</summary>
    /// <param name="recorded">What this identity last put on the broker, or null when nothing has
    /// been.</param>
    /// <remarks>An <see cref="MqttEntity.Include"/> that throws says the capability could not be read.
    /// Reading that as absent would let one unanswered hardware call — a controller busy, a management
    /// interface timing out, a resume from standby — withhold every entity behind it at once. So the
    /// entity keeps the disposition the record holds, and an entity the record has never heard of is
    /// left out rather than announced on the strength of a failed read.</remarks>
    public (IReadOnlyList<MqttEntity> Published, IReadOnlyList<MqttEntity> Withheld) Resolve(
        PublishGroupSnapshot? groups, PublishedDevice? recorded)
    {
        var published = new List<MqttEntity>();
        var withheld = new List<MqttEntity>();

        foreach (var entity in _all)
        {
            switch (entity.IsPublished(groups))
            {
                case true: published.Add(entity); break;
                case false: withheld.Add(entity); break;
                default:
                    if (recorded?.Entities.FirstOrDefault(
                            e => string.Equals(e.EntityId, entity.EntityId, StringComparison.Ordinal))
                        is { } was)
                        (was.Withheld ? withheld : published).Add(entity);
                    break;
            }
        }

        return (published, withheld);
    }

    /// <summary>The state topics the connection publishes on, one per announced entity that has state.
    /// A button contributes none.</summary>
    public static IReadOnlyList<MqttChannel> Channels(IReadOnlyList<MqttEntity> published) =>
        [.. published.Where(e => e.HasState).Select(Channel)];

    /// <summary>The command targets the router resolves against, one per announced entity that takes
    /// commands. An entity that is not announced routes nowhere, so a command addressed to a
    /// switched-off group is reported as unrecognised rather than quietly acted on.</summary>
    public static IReadOnlyList<MqttCommandTarget> CommandTargets(IReadOnlyList<MqttEntity> published) =>
        [.. published.OfType<MqttCommandEntity>().Select(e => new MqttCommandTarget(e.EntityId, e.Accept))];

    private static MqttChannel Channel(MqttEntity entity) =>
        new(entity.EntityId, entity.ReadState, Retain: entity.Retain, Debounce: entity.Debounce);
}
