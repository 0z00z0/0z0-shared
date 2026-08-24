namespace ZeroZero.Mqtt.Discovery;

/// <summary>One entity as it was last put on the broker: what it was announced as, and the topic its
/// value is retained on.</summary>
/// <remarks>The topics are stored composed rather than as their parts. What has to be emptied is
/// exactly what was published, and reading that back from a record is not the same as recomposing it
/// under today's rules — a rule that changed would recompose a topic nothing was ever published
/// on.</remarks>
public sealed class PublishedEntity
{
    public string EntityId { get; set; } = "";

    public string Platform { get; set; } = "";

    /// <summary>Empty when nothing is retained for this entity: a button, an entity that publishes
    /// without retaining, and one that is announced but currently withheld.</summary>
    public string StateTopic { get; set; } = "";

    /// <summary>Whether the entity was announced unavailable rather than published. The disposition,
    /// not a topic: it is what an announcement falls back to when the capability behind the entity
    /// cannot be read, so one unanswered call does not decide it afresh. False on a record written
    /// before this was stored, which is what such a record meant.</summary>
    public bool Withheld { get; set; }
}

/// <summary>One device identity as it was last put on the broker.</summary>
public sealed class PublishedDevice
{
    /// <summary>The identity. Every <c>unique_id</c>, the device block's identifiers and every state
    /// and command topic are composed from this, so a change to it is a different device.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>Where the document was last written. An address, not an identity: the discovery prefix
    /// decides it, and moving the prefix moves the document without touching a single unique id.</summary>
    public string ConfigTopic { get; set; } = "";

    /// <summary>Where the will and its complement were retained.</summary>
    public string AvailabilityTopic { get; set; } = "";

    /// <summary>Where the offline payload is retained for withheld components, or empty when nothing
    /// has been withheld under this identity yet.</summary>
    public string WithheldTopic { get; set; } = "";

    /// <summary>Everything the document names — announced and withheld alike. An entity is in the
    /// document until the entity table stops containing it.</summary>
    public List<PublishedEntity> Entities { get; set; } = [];

    /// <summary>The single-component config topics that have been emptied, composed. Recorded so a
    /// retirement happens once and for good rather than on every connect.</summary>
    /// <remarks>Composed topics, never ids. The component segment is what keeps one component's
    /// retirement off another component's live config, and an id on its own has thrown it away —
    /// which is the whole of what stands between a consumer that legitimately publishes
    /// <c>switch/x</c> while retiring <c>binary_sensor/x</c> and a silent deletion.</remarks>
    public List<string> Retired { get; set; } = [];

    /// <summary>The single-component config topics already handed over to the device document,
    /// composed. Kept apart from <see cref="Retired"/> because the two write the same topic with
    /// opposite intent: replaying a migration as a retirement would remove what the handover kept, and
    /// would do so on the first restart after adoption.</summary>
    public List<string> Migrated { get; set; } = [];
}

/// <summary>What this installation last put on the broker, across every identity it has published
/// under.</summary>
/// <remarks>
/// <para>Diffing a new entity set against the previous one held in memory is correct only for a fixed
/// table. An entity removed while the application was closed is never diffed at all: nothing on the
/// next run knows it ever existed, and the retained config and state topics it left behind stay on
/// the broker for ever. A per-machine entity set — one entry per virtual machine, per drive, per
/// adapter — reaches that case on the first removal.</para>
/// <para>So what was published is written down, and the next connect reconciles against the record
/// rather than against memory. The record also carries why something stopped being published, which
/// is what keeps a reversible state — a group switched off, a drive unplugged — from being announced
/// as a permanent removal.</para>
/// </remarks>
public sealed class DiscoveryLedger
{
    public List<PublishedDevice> Devices { get; set; } = [];

    /// <summary>The record for one device id, or null if nothing has been published under it.</summary>
    public PublishedDevice? Find(string deviceId) =>
        Devices.FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));

    /// <summary>A detached copy, so a caller reading the ledger never holds what a writer mutates. A
    /// record written before the device id was stored alongside the topic has it derived from the
    /// topic, so an existing installation keeps its identity rather than looking like a new one.</summary>
    public DiscoveryLedger Copy() => new()
    {
        Devices =
        [
            .. Devices.Select(d => new PublishedDevice
            {
                DeviceId = d.DeviceId is { Length: > 0 } id
                    ? id
                    : DiscoveryTopics.DeviceIdOf(d.ConfigTopic) ?? "",
                ConfigTopic = d.ConfigTopic,
                AvailabilityTopic = d.AvailabilityTopic,
                WithheldTopic = d.WithheldTopic,
                Entities =
                [
                    .. d.Entities.Select(e => new PublishedEntity
                    {
                        EntityId = e.EntityId,
                        Platform = e.Platform,
                        StateTopic = e.StateTopic,
                        Withheld = e.Withheld,
                    }),
                ],
                Retired = [.. d.Retired],
                Migrated = [.. d.Migrated],
            }),
        ],
    };
}

/// <summary>Where the ledger is kept. Two members, and no assumption that the module owns the file
/// behind them.</summary>
/// <remarks><see cref="Read"/> hands back a snapshot: mutating it changes nothing.
/// <see cref="Update"/> is read-modify-write against the live state, as <c>IMqttSettingsStore</c> is
/// and for the same reason — a caller holding a snapshot must not roll back what a sibling wrote
/// meanwhile.</remarks>
public interface IDiscoveryLedgerStore
{
    DiscoveryLedger Read();

    void Update(Action<DiscoveryLedger> mutate);
}

/// <summary>The ledger for the life of the process and no longer. The deliberate opt-out, never a
/// default: without a durable store an entity removed while the application was closed is never
/// evicted, a retirement is replayed on every start, and a migration is replayed as a retirement,
/// which removes what the handover kept.</summary>
/// <remarks>Right for a test, and for a host that genuinely has nowhere to write.
/// <see cref="DiscoveryLedgerFile.In"/> is one line and has none of these properties.</remarks>
public sealed class TransientLedgerStore : IDiscoveryLedgerStore
{
    private readonly Lock _gate = new();
    private DiscoveryLedger _ledger = new();

    public DiscoveryLedger Read()
    {
        lock (_gate) return _ledger.Copy();
    }

    public void Update(Action<DiscoveryLedger> mutate)
    {
        lock (_gate) mutate(_ledger);
    }
}
