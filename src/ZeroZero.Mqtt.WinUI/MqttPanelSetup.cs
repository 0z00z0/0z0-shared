using ZeroZero.Primitives;

namespace ZeroZero.Mqtt.WinUI;

/// <summary>
/// Everything <see cref="MqttSettingsPanel"/> needs, in one object initialiser. Assigned once and
/// passed to <see cref="MqttSettingsPanel.Initialise"/>; the panel never reaches past it.
/// </summary>
/// <remarks>
/// <para>Every member is read and every callback is raised on the UI thread the panel lives on, with
/// two exceptions named on the members themselves: <see cref="PublishNow"/> is awaited, so its own
/// body may leave that thread, and <see cref="ConnectionState"/> is called from the UI thread but
/// answers about a connection that lives on another, so it must be safe to ask at any moment.</para>
/// <para>The split of what the host supplies is not arbitrary. Protocol vocabulary is identical for
/// every consumer and is the module's, so no host writes what a transport is or what the discovery
/// prefix controls. What an application publishes is the opposite — the module knows none of it,
/// including how to describe the publish section as a whole.</para>
/// </remarks>
public sealed class MqttPanelSetup
{
    /// <summary>The module's entire storage dependency. The panel reads through it and writes through
    /// it; it never sees a settings file or a host's own settings class.</summary>
    public required IMqttSettingsStore Settings { get; init; }

    /// <summary>The application-declared publish groups and their current state. The panel renders
    /// one row per declared group and invents no group vocabulary of its own.</summary>
    public required PublishGroupSet Groups { get; init; }

    /// <summary>The topic root, needed only to derive the default device id alongside the machine
    /// name. The panel composes no other topic.</summary>
    public required string TopicRoot { get; init; }

    /// <summary>When something last reached the broker, and what the broker last asked for. Written
    /// from the MQTT threads, read here from the UI thread; each slot is swapped atomically.</summary>
    public required MqttActivity Activity { get; init; }

    /// <summary>What the connection is doing. Asked rather than held: the link comes and goes on its
    /// own, so a cached answer is stale the moment the page stops looking.</summary>
    public required Func<MqttConnectionState> ConnectionState { get; init; }

    /// <summary>The on-demand republish behind "Publish now". Awaited on the UI thread; the
    /// continuation resumes there. False means nothing reached the broker.</summary>
    public required Func<Task<bool>> PublishNow { get; init; }

    /// <summary>Raised when something the connection is built from has been committed — the master
    /// switch, the Apply batch, the device name, or the device id. Exactly one reconnect attempt per
    /// raise.</summary>
    /// <remarks>A device-id change reaches this too, and the panel's confirmation dialogue promises
    /// the old entities are removed. The module can keep that promise: the discovery ledger evicts a
    /// superseded identity by what it actually published, including across a restart. A host wiring
    /// this to something that does not run the connection's apply path breaks a promise the panel has
    /// already made on its behalf.</remarks>
    public required Action ConnectionChanged { get; init; }

    /// <summary>Raised when what is announced changes — here, a publishing group toggled. The
    /// announced entity set is baked into the retained discovery document, so without this the broker
    /// keeps the set captured at connect time.</summary>
    public required Action PublishSetChanged { get; init; }

    /// <summary>Where the broker last answered, so the Status rows can say what the connection landed
    /// on. Read-only: the panel never writes endpoint memory, not even after a successful test.</summary>
    /// <remarks>The same accessor the connection is given through
    /// <see cref="MqttConnectionSetup.RecallEndpoint"/>. Reading it here and writing it there is what
    /// keeps a test connection from changing the sweep order of the live one.</remarks>
    public Func<MqttEndpointMemory?>? RecallEndpoint { get; init; }

    /// <summary>The display name published for this machine when the Device name box is empty, shown
    /// as that box's placeholder. Must be the same expression the publisher falls back to, or the
    /// placeholder promises a name that is never used.</summary>
    public string? DefaultDeviceName { get; init; }

    /// <summary>The master switch card's header. The module supplies a fallback because a host that
    /// says nothing still needs a card with a name on it.</summary>
    public string? PublishTitle { get; init; }

    /// <summary>The one line under the master switch — the only place the panel says what this
    /// application publishes.</summary>
    public string? PublishDescription { get; init; }

    /// <summary>The master switch's info icon.</summary>
    public string? PublishInfo { get; init; }

    /// <summary>The "What to publish" heading's info icon. Consumer-owned with no fallback: the
    /// module knows nothing about what an application publishes, including how to describe the
    /// section as a whole. Left unset, the heading carries no icon rather than an empty one.</summary>
    public string? PublishGroupsInfo { get; init; }

    /// <summary>An application-specific consequence of renaming the device, shown in the change
    /// dialogue under the module's own. Left unset, the dialogue says only what the module can always
    /// stand behind.</summary>
    public string? DeviceIdConsequence { get; init; }

    /// <summary>Turns an entity id into the name a user would recognise, for the last-command row.
    /// Without one the entity id stands, which is what the wire actually carried.</summary>
    public Func<string, string>? CommandLabel { get; init; }

    /// <summary>Where the module's own strings are translated. Null takes the module's resource map,
    /// and anything that map does not answer falls back to the built-in en-GB — so a host that ships
    /// no translation, and one whose resource map fails to load, both get a readable panel.</summary>
    public IMqttStringSource? Strings { get; init; }

    /// <summary>Where the panel's guarded handlers report. Defaults to the no-op.</summary>
    public ILogSink Log { get; init; } = NullLogSink.Instance;

    /// <summary>The default device name, resolved. One expression, so the placeholder and whatever
    /// the publisher falls back to cannot disagree.</summary>
    internal string ResolvedDefaultDeviceName =>
        DefaultDeviceName ?? $"{TopicRoot} ({Environment.MachineName})";
}
