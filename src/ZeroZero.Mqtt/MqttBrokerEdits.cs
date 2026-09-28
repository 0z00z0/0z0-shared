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

    public MqttCertificateTrustMode TrustMode { get; set; } = MqttCertificateTrustMode.System;

    /// <summary>The thumbprint or the base64 certificate the two pinned modes need. Held as text so
    /// a half-pasted value is a state rather than a parse failure, exactly as the typed port is, and
    /// as one box because only the chosen mode's value is ever committed.</summary>
    public string TrustValue { get; set; } = "";

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

    /// <summary>The staged certificate trust, as a connection reads it. Never read without
    /// <see cref="Validate"/> having gated the action first, or a pinned mode with nothing pinned
    /// reaches the handshake as a refused certificate rather than a message beside the box.</summary>
    public MqttCertificateTrust Trust => StagedTrust(TrustMode, TrustValue);

    /// <summary>Why the block cannot be committed, and whether it can be. One answer for Apply and
    /// for Test, and the message is the first field at fault in the order the fields are read.</summary>
    public MqttEditValidation Validate()
    {
        var port = ValidatePort();
        return port.Usable ? ValidateTrust() : port;
    }

    /// <summary>The typed port's own answer, for the message that belongs beside that box.</summary>
    public MqttEditValidation ValidatePort()
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

    /// <summary>The staged trust's own answer, for the message that belongs beside its box. Only the
    /// two pinned modes can be unusable; the platform's own trust and accepting any certificate need
    /// no value and are always ready to apply.</summary>
    public MqttEditValidation ValidateTrust()
    {
        if (TrustMode is not (MqttCertificateTrustMode.Thumbprint or MqttCertificateTrustMode.Certificate))
            return new(null, true);

        // Same rule as the typed port: a box not yet typed into is not a mistake, and is not a pin
        // either, so nothing may run on it.
        if (string.IsNullOrWhiteSpace(TrustValue)) return new(null, false);

        if (Trust.Validate() is null) return new(null, true);

        // The trust setting's own reason is the module's internal answer and is never shown; the
        // message beside the box comes from the string table, as every other one does.
        return new(_text.Get(TrustMode == MqttCertificateTrustMode.Thumbprint
                                ? "TrustNotAThumbprint" : "TrustNotACertificate"),
                   false);
    }

    // Only the value belonging to the chosen mode is carried, so a value left behind by a mode that
    // is no longer selected is neither committed nor counted as an unapplied edit.
    private static MqttCertificateTrust StagedTrust(MqttCertificateTrustMode mode, string value) => mode switch
    {
        MqttCertificateTrustMode.Thumbprint  => MqttCertificateTrust.ForThumbprint(value),
        MqttCertificateTrustMode.Certificate => MqttCertificateTrust.ForCertificate(value),
        MqttCertificateTrustMode.AcceptAny   => MqttCertificateTrust.AcceptAny,
        _ => MqttCertificateTrust.SystemTrust,
    };

    private static MqttCertificateTrust StagedTrust(MqttCertificateTrust trust) =>
        StagedTrust(trust.Mode, TrustValueOf(trust));

    private static string TrustValueOf(MqttCertificateTrust trust) => trust.Mode switch
    {
        MqttCertificateTrustMode.Thumbprint  => trust.Thumbprint,
        MqttCertificateTrustMode.Certificate => trust.Certificate,
        _ => "",
    };

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
        || Trust != StagedTrust(_saved.CertificateTrust)
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
        SelectTrust(_saved.CertificateTrust);
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
        if (Trust == StagedTrust(previous.CertificateTrust)) SelectTrust(_saved.CertificateTrust);
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

    /// <summary>Puts a saved trust setting on the staged mode and its one box: the pinned value for
    /// a pinned mode, and an empty box for the two modes that pin nothing.</summary>
    public void SelectTrust(MqttCertificateTrust trust)
    {
        TrustMode = trust.Mode;
        TrustValue = TrustValueOf(trust);
    }

    /// <summary>Writes the whole staged block onto a settings record, and takes it as the new
    /// baseline. One mutation, so the connection is remade once for the batch.</summary>
    /// <remarks>Refuses rather than rounding when <see cref="Validate"/> says the block is unusable:
    /// an out-of-range port must never be quietly saved as something else, and must never collapse to
    /// Automatic behind the user's back, and a pinned mode with nothing pinned must never be saved as
    /// the platform's own trust.</remarks>
    public bool Apply(IMqttSettingsStore store)
    {
        if (!Validate().Usable) return false;

        string host = Host.Trim();
        int? port = Port;
        string username = Username.Trim();
        string password = Password;
        var transport = Transport;
        var encryption = Encryption;
        var trust = Trust;
        string prefix = EffectivePrefix;

        store.Update(s =>
        {
            s.Host = host;
            s.Port = port;
            s.Username = username;
            s.Password = password;
            s.TransportMode = transport;
            s.EncryptionMode = encryption;
            s.CertificateTrust = trust;
            s.DiscoveryPrefix = prefix;
        });

        _saved = store.Read().Copy();
        Load(_saved);
        State = MqttEditState.Applied;
        return true;
    }
}
