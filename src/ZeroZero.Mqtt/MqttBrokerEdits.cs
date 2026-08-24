using System.Globalization;

namespace ZeroZero.Mqtt;

/// <summary>How the port is being chosen: found by probing, picked from the offered list, or typed.</summary>
public enum MqttPortMode
{
    /// <summary>Nothing pinned — the sweep finds it.</summary>
    Automatic,

    /// <summary>One of <see cref="MqttEndpointPlan.OfferedPorts"/>.</summary>
    Offered,

    /// <summary>A number typed by hand, which is the only one that can be invalid.</summary>
    Custom,
}

/// <summary>What the applied indicator beside the Apply button says.</summary>
public enum MqttEditState
{
    /// <summary>The staged values are the saved values and nothing has been applied this session.</summary>
    Clean,

    /// <summary>Something has been typed that is not live. The one state a collapsed group must not
    /// be able to hide.</summary>
    Edited,

    /// <summary>An Apply committed the block and nothing has moved since.</summary>
    Applied,
}

/// <summary>Why the staged block cannot be committed, and whether it can be committed at all.</summary>
/// <param name="Message">What to show beside the field at fault, or null when there is nothing to
/// say. A box not yet typed into is not a mistake, so it carries no message while still being
/// unusable.</param>
/// <param name="Usable">The single gate. Apply and Test both read this one answer, so a green test
/// can never vouch for a configuration Apply would refuse.</param>
public readonly record struct MqttEditValidation(string? Message, bool Usable);

/// <summary>The Broker block's staged values, the saved values behind them, and which of the two is
/// live. Pure: no controls, no store, no clock — a panel mirrors its controls onto this and reads
/// back what to render.</summary>
/// <remarks>
/// <para>Staging exists so the connection is remade once per edit session rather than once per
/// keystroke. That makes an un-applied edit a real state, and one that has to be visible from outside
/// whatever group holds the fields: an edit a collapsed expander hides is an edit that is lost at the
/// next reload without anything having said so.</para>
/// <para><see cref="Reload"/> is a three-way merge rather than an overwrite for the same reason. A
/// host re-reading its store while the panel is on screen must not discard what is being typed, and a
/// field nobody has touched must still pick up a value a sibling changed.</para>
/// </remarks>
public sealed class MqttBrokerEdits
{
    /// <summary>The lowest and highest a typed port may be.</summary>
    public const int PortMin = 1;

    public const int PortMax = 65535;

    private readonly MqttStrings _text;
    private MqttSettings _saved = new();

    public MqttBrokerEdits(MqttStrings? text = null)
    {
        _text = text ?? MqttStrings.Default;
        Load(new MqttSettings());
    }

    // ------------------------------------------------------------------------------------------
    // The staged values. Everything a panel's Broker group edits, and nothing else.
    // ------------------------------------------------------------------------------------------

    public string Host { get; set; } = "";

    public MqttPortMode PortMode { get; set; } = MqttPortMode.Automatic;

    /// <summary>The chosen entry while <see cref="PortMode"/> is <see cref="MqttPortMode.Offered"/>.</summary>
    public int OfferedPort { get; set; } = MqttEndpointPlan.OfferedPorts[0];

    /// <summary>The typed entry while <see cref="PortMode"/> is <see cref="MqttPortMode.Custom"/>.
    /// Held as text so a half-typed number is a state rather than a parse failure.</summary>
    public string TypedPort { get; set; } = "";

    public MqttTransportMode Transport { get; set; } = MqttTransportMode.Auto;

    public MqttEncryptionMode Encryption { get; set; } = MqttEncryptionMode.Auto;

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public string DiscoveryPrefix { get; set; } = MqttSettings.DefaultDiscoveryPrefix;

    // ------------------------------------------------------------------------------------------
    // Reading the staged block.
    // ------------------------------------------------------------------------------------------

    /// <summary>The staged port, or null for Automatic — and null too for a typed value that does not
    /// validate, so nothing downstream ever sees an out-of-range port. Never read without
    /// <see cref="Validate"/> having gated the action first, or an invalid entry silently becomes a
    /// sweep.</summary>
    public int? Port => PortMode switch
    {
        MqttPortMode.Offered => OfferedPort,
        MqttPortMode.Custom  => Parse(TypedPort, out int typed) ? typed : null,
        _ => null,
    };

    /// <summary>The staged block as the pure endpoint plan reads it. No password: nothing the plan
    /// decides depends on one.</summary>
    public MqttEndpointRequest Request =>
        new(Host.Trim(), Username.Trim(), Port, Transport, Encryption);

    /// <summary>Why the block cannot be committed, and whether it can be. One answer for Apply and
    /// for Test.</summary>
    public MqttEditValidation Validate()
    {
        if (PortMode != MqttPortMode.Custom) return new(null, true);

        // An empty box before the first keystroke is not yet a mistake, so it carries no message —
        // but it is not a port either, so nothing may run on it.
        if (string.IsNullOrWhiteSpace(TypedPort)) return new(null, false);

        return Parse(TypedPort, out _)
            ? new(null, true)
            : new(TypedPort.Trim().All(char.IsAsciiDigit)
                    ? _text.Format("PortOutOfRange", PortMin, PortMax)
                    : _text.Format("PortNotANumber", PortMin, PortMax),
                  false);
    }

    // NumberStyles.None and InvariantCulture together, so no thousands separator, sign or whitespace
    // can slip a value past the range check on a culture that allows one.
    private static bool Parse(string? text, out int port) =>
        int.TryParse((text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)
        && port is >= PortMin and <= PortMax;

    // ------------------------------------------------------------------------------------------
    // Staged against saved.
    // ------------------------------------------------------------------------------------------

    /// <summary>What the applied indicator says.</summary>
    public MqttEditState State { get; private set; } = MqttEditState.Clean;

    /// <summary>Whether anything staged differs from what is live. The flag a panel renders outside
    /// any collapsed group holding these fields.</summary>
    public bool IsDirty =>
        Host.Trim() != _saved.Host
        || Port != _saved.Port
        || Transport != _saved.TransportMode
        || Encryption != _saved.EncryptionMode
        || Username.Trim() != _saved.Username
        || Password != _saved.Password
        || DiscoveryPrefix.Trim() != _saved.DiscoveryPrefix;

    /// <summary>The prefix as it would be committed: a blank box means the default rather than an
    /// empty prefix, which would put every discovery topic at the root.</summary>
    /// <remarks>Only Apply reads this. Whether the block is dirty compares the box as typed, or a
    /// store holding a blank prefix would open the panel already marked unapplied.</remarks>
    public string EffectivePrefix => string.IsNullOrWhiteSpace(DiscoveryPrefix)
        ? MqttSettings.DefaultDiscoveryPrefix
        : DiscoveryPrefix.Trim();

    /// <summary>Recomputes <see cref="State"/> after a staged value moved. A change that puts a field
    /// back where it started leaves the indicator alone rather than clearing an "Applied." that is
    /// still true.</summary>
    public void Touch()
    {
        if (IsDirty) State = MqttEditState.Edited;
        else if (State == MqttEditState.Edited) State = MqttEditState.Clean;
    }

    /// <summary>Takes the saved block as both the staged values and the baseline. The first read, and
    /// the explicit discard — never something a mere re-read does.</summary>
    public void Load(MqttSettings saved)
    {
        _saved = saved.Copy();
        Host            = _saved.Host;
        Transport       = _saved.TransportMode;
        Encryption      = _saved.EncryptionMode;
        Username        = _saved.Username;
        Password        = _saved.Password;
        DiscoveryPrefix = _saved.DiscoveryPrefix;
        SelectPort(_saved.Port);
        State = MqttEditState.Clean;
    }

    /// <summary>Re-reads the store without discarding what is being typed: a field that has been
    /// edited keeps the edit, a field that has not takes whatever the store now says.</summary>
    /// <remarks>The overwrite this replaces is how a settings window re-shown while already open
    /// threw away a typed host with nothing on screen having warned about it.</remarks>
    public void Reload(MqttSettings saved)
    {
        var previous = _saved;
        _saved = saved.Copy();

        if (Host.Trim() == previous.Host) Host = _saved.Host;
        if (Port == previous.Port) SelectPort(_saved.Port);
        if (Transport == previous.TransportMode) Transport = _saved.TransportMode;
        if (Encryption == previous.EncryptionMode) Encryption = _saved.EncryptionMode;
        if (Username.Trim() == previous.Username) Username = _saved.Username;
        if (Password == previous.Password) Password = _saved.Password;
        if (DiscoveryPrefix.Trim() == previous.DiscoveryPrefix) DiscoveryPrefix = _saved.DiscoveryPrefix;

        // An "Applied." from before the re-read is about values that may no longer be the saved ones,
        // so it stands only while nothing differs.
        if (IsDirty) State = MqttEditState.Edited;
        else if (State == MqttEditState.Edited) State = MqttEditState.Clean;
    }

    /// <summary>Puts a saved port on the three-way selection: Automatic for none, the matching entry
    /// for one the list offers, and the typed box for anything else.</summary>
    public void SelectPort(int? port)
    {
        if (port is not { } value)
        {
            PortMode = MqttPortMode.Automatic;
            TypedPort = "";
            return;
        }

        if (MqttEndpointPlan.OfferedPorts.Contains(value))
        {
            PortMode = MqttPortMode.Offered;
            OfferedPort = value;
            TypedPort = "";
            return;
        }

        PortMode = MqttPortMode.Custom;
        TypedPort = value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Writes the whole staged block onto a settings record, and takes it as the new
    /// baseline. One mutation, so the connection is remade once for the batch.</summary>
    /// <remarks>Refuses rather than rounding when <see cref="Validate"/> says the block is unusable:
    /// an out-of-range port must never be quietly saved as something else, and must never collapse to
    /// Automatic behind the user's back.</remarks>
    public bool Apply(IMqttSettingsStore store)
    {
        if (!Validate().Usable) return false;

        string host = Host.Trim();
        int? port = Port;
        string username = Username.Trim();
        string password = Password;
        var transport = Transport;
        var encryption = Encryption;
        string prefix = EffectivePrefix;

        store.Update(s =>
        {
            s.Host = host;
            s.Port = port;
            s.Username = username;
            s.Password = password;
            s.TransportMode = transport;
            s.EncryptionMode = encryption;
            s.DiscoveryPrefix = prefix;
        });

        _saved = store.Read().Copy();
        Load(_saved);
        State = MqttEditState.Applied;
        return true;
    }
}
