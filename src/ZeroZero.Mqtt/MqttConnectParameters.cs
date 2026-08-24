using System.Security.Cryptography;
using System.Text;

namespace ZeroZero.Mqtt;

/// <summary>Everything one connect round needs, and nothing a connection must not reconfigure itself
/// over. What <see cref="MqttConnection.Apply"/> takes.</summary>
/// <remarks>
/// <para>Separate from <see cref="MqttSettings"/> rather than a rename of it. Three things the
/// persisted record carries are deliberately absent: the group state, because a group toggle should
/// republish and never bounce the socket; where the broker last answered, because writing that on a
/// successful connect would otherwise make the connect a settings change and reconnect on the
/// strength of its own success; and the password, which is reached through
/// <see cref="Password"/> so a value that is compared, logged or passed between threads never carries
/// a secret.</para>
/// <para>Two projections built from equal settings are equal, and <see cref="MqttConnection.Apply"/>
/// does nothing when handed one it already has. That is what makes applying on every settings change
/// safe.</para>
/// </remarks>
public sealed record MqttConnectParameters
{
    public bool Enabled { get; init; }

    public string Host { get; init; } = "";

    public int? Port { get; init; }

    public MqttTransportMode TransportMode { get; init; } = MqttTransportMode.Auto;

    public MqttEncryptionMode EncryptionMode { get; init; } = MqttEncryptionMode.Auto;

    public MqttCertificateTrust CertificateTrust { get; init; } = MqttCertificateTrust.SystemTrust;

    public string Username { get; init; } = "";

    /// <summary>Which secret applies, without being it. Two projections differing only here are
    /// different connections, so a changed password reconnects; a host with a credential store puts
    /// its key here, and <see cref="MqttSettings.Connect"/> puts a fingerprint of the stored
    /// password.</summary>
    public string CredentialRef { get; init; } = "";

    /// <summary>Fetched when a candidate's options are built, never held as a value. Excluded from
    /// equality: <see cref="CredentialRef"/> is what says the secret changed.</summary>
    public Func<string> Password { get; init; } = static () => "";

    public string DeviceId { get; init; } = "";

    public string DeviceName { get; init; } = "";

    public string DiscoveryPrefix { get; init; } = MqttSettings.DefaultDiscoveryPrefix;

    /// <summary>The staged choices as the pure endpoint plan reads them.</summary>
    public MqttEndpointRequest Request =>
        new(Host, Username, Port, TransportMode, EncryptionMode);

    /// <summary>Whether there is anything to connect to at all.</summary>
    public bool ShouldRun => Enabled && !string.IsNullOrWhiteSpace(Host);

    /// <summary>A short, non-reversible stand-in for a secret, so a change to one is observable
    /// without the secret itself being carried. Never persisted and never logged.</summary>
    public static string Fingerprint(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return "";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    public bool Equals(MqttConnectParameters? other) =>
        other is not null
        && Enabled == other.Enabled
        && string.Equals(Host, other.Host, StringComparison.Ordinal)
        && Port == other.Port
        && TransportMode == other.TransportMode
        && EncryptionMode == other.EncryptionMode
        && CertificateTrust == other.CertificateTrust
        && string.Equals(Username, other.Username, StringComparison.Ordinal)
        && string.Equals(CredentialRef, other.CredentialRef, StringComparison.Ordinal)
        && string.Equals(DeviceId, other.DeviceId, StringComparison.Ordinal)
        && string.Equals(DeviceName, other.DeviceName, StringComparison.Ordinal)
        && string.Equals(DiscoveryPrefix, other.DiscoveryPrefix, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Enabled);
        hash.Add(Host, StringComparer.Ordinal);
        hash.Add(Port);
        hash.Add(TransportMode);
        hash.Add(EncryptionMode);
        hash.Add(CertificateTrust);
        hash.Add(Username, StringComparer.Ordinal);
        hash.Add(CredentialRef, StringComparer.Ordinal);
        hash.Add(DeviceId, StringComparer.Ordinal);
        hash.Add(DeviceName, StringComparer.Ordinal);
        hash.Add(DiscoveryPrefix, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
