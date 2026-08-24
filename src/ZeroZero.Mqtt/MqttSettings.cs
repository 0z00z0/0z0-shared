namespace ZeroZero.Mqtt;

/// <summary>The persisted broker block. A plain mutable class with a parameterless constructor,
/// JSON-serialisable, holding nothing application-shaped.</summary>
/// <remarks>What is stored and what is connected with are deliberately different types.
/// <see cref="Connect"/> projects this onto <see cref="MqttConnectParameters"/>, and the projection
/// leaves out everything a connection must not reconfigure itself over: the password, the group
/// state, and where the broker last answered.</remarks>
public sealed class MqttSettings
{
    /// <summary>Inert until this is on AND a broker host is set — the module never touches the
    /// network otherwise.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    /// <summary>The broker port, or null to find it. Null by default: the port a broker answers on
    /// depends on the transport and on what fronts it, so assuming 1883 is wrong everywhere except
    /// the plain internal case.</summary>
    public int? Port { get; set; }

    /// <summary>Which transport the broker is reached over. Auto probes; an explicit choice is never
    /// overridden, so a machine pinned to one path fails loudly rather than connecting another way.
    /// The port applies to both transports — see <see cref="MqttEndpoint"/>.</summary>
    public MqttTransportMode TransportMode { get; set; } = MqttTransportMode.Auto;

    /// <summary>Whether the link to the broker is encrypted. Automatic tries encrypted first and
    /// falls back to plain only where nothing was listening; an explicit choice is never probed
    /// around, exactly as for the port and the transport.</summary>
    public MqttEncryptionMode EncryptionMode { get; set; } = MqttEncryptionMode.Auto;

    /// <summary>Which certificate an encrypted link accepts. A setting rather than a hook: forcing
    /// encryption on against a broker with a self-signed certificate cannot connect under system
    /// trust alone.</summary>
    public MqttCertificateTrust CertificateTrust { get; set; } = MqttCertificateTrust.SystemTrust;

    public string Username { get; set; } = "";

    /// <summary>Clear text. Protection slots in as one protector applied inside the settings file's
    /// serialise and deserialise path, so turning it on changes no call site.</summary>
    public string Password { get; set; } = "";

    /// <summary>The MQTT client id, the <c>unique_id</c> stem, the device identifier and every topic
    /// segment below the root. Empty = derived from the topic root and the machine name; changing it
    /// evicts the old id's retained topics so a consumer deletes the previous device instead of
    /// ghosting it.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>Empty = the application's own default, from
    /// <see cref="MqttConnectionSetup.DefaultDeviceName"/>.</summary>
    public string DeviceName { get; set; } = "";

    /// <summary>The one place the module names a receiver, and only as a default a host may
    /// change.</summary>
    public const string DefaultDiscoveryPrefix = "homeassistant";

    /// <summary>Must match the receiver's own prefix, or discovery documents land where nothing reads
    /// them. Never read by the connection itself, which treats it as inert and passes it through.</summary>
    public string DiscoveryPrefix { get; set; } = DefaultDiscoveryPrefix;

    /// <summary>Which publish groups are switched on, by group key. A key absent from the dictionary
    /// takes the group's own declared default, so a group added in a later version starts where its
    /// author intended rather than off.</summary>
    public Dictionary<string, bool> Groups { get; set; } = new(StringComparer.Ordinal);

    /// <summary>What the connection is applied with. The projection carries no password, no group
    /// state and no endpoint memory: a group toggle and a successful connect both leave it identical,
    /// so neither can be mistaken for "the broker settings changed".</summary>
    public MqttConnectParameters Connect()
    {
        string password = Password;
        return new()
        {
            Enabled = Enabled,
            Host = Host,
            Port = Port,
            TransportMode = TransportMode,
            EncryptionMode = EncryptionMode,
            CertificateTrust = CertificateTrust,
            Username = Username,
            CredentialRef = MqttConnectParameters.Fingerprint(password),
            Password = () => password,
            DeviceId = DeviceId,
            DeviceName = DeviceName,
            DiscoveryPrefix = DiscoveryPrefix,
        };
    }

    /// <summary>A copy, so a caller staging edits never hands the live instance to a writer.</summary>
    public MqttSettings Copy() => new()
    {
        Enabled = Enabled,
        Host = Host,
        Port = Port,
        TransportMode = TransportMode,
        EncryptionMode = EncryptionMode,
        CertificateTrust = CertificateTrust,
        Username = Username,
        Password = Password,
        DeviceId = DeviceId,
        DeviceName = DeviceName,
        DiscoveryPrefix = DiscoveryPrefix,
        Groups = new Dictionary<string, bool>(Groups, StringComparer.Ordinal),
    };
}

/// <summary>The module's entire storage dependency.</summary>
/// <remarks>
/// <para>Three members, and no assumption that the module owns the file behind them.
/// <see cref="MqttSettingsFile"/> is the ready-made implementation for a host that wants its own
/// <c>mqtt.json</c>; a host whose configuration is one document with several unrelated sections
/// implements these three over that document instead, and the module is none the wiser.</para>
/// <para><see cref="Update"/> is read-modify-write against the live state on purpose. A caller
/// holding a snapshot — a settings panel opened some time ago — must be able to commit one field
/// without rolling back whatever a sibling changed meanwhile.</para>
/// <para><see cref="Changed"/> promises nothing about ordering, thread affinity or coalescing, and a
/// subscriber that does real work must assume all three. What it must not do is fire while the
/// store's own write lock is held: a subscriber's handler can block.</para>
/// </remarks>
public interface IMqttSettingsStore
{
    MqttSettings Read();

    void Update(Action<MqttSettings> mutate);

    event Action? Changed;
}
