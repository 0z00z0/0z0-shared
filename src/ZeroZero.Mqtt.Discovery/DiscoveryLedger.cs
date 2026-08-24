namespace ZeroZero.Mqtt.Discovery;

/// <summary>One entity as it was last put on the broker: what it was called, what platform it was
/// announced as, and the topic its value was retained on.</summary>
/// <remarks>The topics are stored composed rather than as their parts. What has to be emptied is
/// exactly what was published, and reading that back from a record is not the same as recomposing it
/// under today's rules — a rule that changed would recompose a topic nothing was ever published
/// on.</remarks>
public sealed class PublishedEntity
{
    public string EntityId { get; set; } = "";

    public string Platform { get; set; } = "";

    /// <summary>Empty for an entity that has no state topic — a button.</summary>
    public string StateTopic { get; set; } = "";
}

/// <summary>One device identity as it was last put on the broker.</summary>
public sealed class PublishedDevice
{
    /// <summary>The device document's own topic. Also the identity: it carries both the discovery
    /// prefix and the device id, and a change to either is a different device.</summary>
    public string ConfigTopic { get; set; } = "";

    /// <summary>Where the will and its complement were retained.</summary>
    public string AvailabilityTopic { get; set; } = "";

    public List<PublishedEntity> Entities { get; set; } = [];
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
/// rather than against memory. Everything the record names and the configuration no longer publishes
/// is evicted, whether or not this process is the one that published it.</para>
/// </remarks>
public sealed class DiscoveryLedger
{
    public List<PublishedDevice> Devices { get; set; } = [];

    /// <summary>The record for one device document, or null if nothing has been published under it.</summary>
    public PublishedDevice? Find(string configTopic) =>
        Devices.FirstOrDefault(d => string.Equals(d.ConfigTopic, configTopic, StringComparison.Ordinal));

    /// <summary>A detached copy, so a caller reading the ledger never holds what a writer mutates.</summary>
    public DiscoveryLedger Copy() => new()
    {
        Devices =
        [
            .. Devices.Select(d => new PublishedDevice
            {
                ConfigTopic = d.ConfigTopic,
                AvailabilityTopic = d.AvailabilityTopic,
                Entities =
                [
                    .. d.Entities.Select(e => new PublishedEntity
                    {
                        EntityId = e.EntityId,
                        Platform = e.Platform,
                        StateTopic = e.StateTopic,
                    }),
                ],
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

/// <summary>The ledger for the life of the process and no longer. The default, so a consumer that
/// has wired nothing still runs — and the one configuration in which eviction does not survive a
/// restart.</summary>
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
