using ZeroZero.Config;

namespace ZeroZero.Mqtt.Discovery;

/// <summary>The ledger over one JSON file. The module owns the file name; the host owns the
/// directory.</summary>
/// <remarks>
/// <para>It sits beside the broker settings rather than inside them: what was published is state the
/// layer discovers, and writing it as a setting would make a successful announcement look like a
/// settings change to anything listening for one.</para>
/// <para>A host whose configuration is a single document implements
/// <see cref="IDiscoveryLedgerStore"/>'s two members over that document instead and never constructs
/// this class. A host that constructs neither gets <see cref="TransientLedgerStore"/> and loses
/// eviction across a restart.</para>
/// </remarks>
public sealed class DiscoveryLedgerFile : IDiscoveryLedgerStore
{
    /// <summary>The name the module owns.</summary>
    public const string DefaultFileName = "mqtt-discovery.json";

    private readonly SettingsFile<DiscoveryLedger> _file;

    public DiscoveryLedgerFile(SettingsFileOptions options) => _file = new(options);

    /// <summary>The whole of the wiring: one line, and eviction survives a restart.</summary>
    public static DiscoveryLedgerFile In(string directory) =>
        new(new SettingsFileOptions(directory, DefaultFileName));

    /// <summary>The file behind the ledger, for a host that wants to hear about a failed write.</summary>
    public SettingsFile<DiscoveryLedger> File => _file;

    public string FilePath => _file.FilePath;

    public DiscoveryLedger Read() => _file.Read();

    public void Update(Action<DiscoveryLedger> mutate) => _file.Update(mutate);
}
