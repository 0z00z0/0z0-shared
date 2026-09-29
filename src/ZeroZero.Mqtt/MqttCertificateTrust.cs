using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace ZeroZero.Mqtt;

/// <summary>Which certificates an encrypted link will accept.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MqttCertificateTrustMode
{
    /// <summary>Only a certificate the operating system's own stores already trust.</summary>
    System,

    /// <summary>Whatever the far end presents. The link is still encrypted, and no longer proves
    /// which machine is at the other end.</summary>
    AcceptAny,
}

/// <summary>What a broker presented, reduced to the one fact a trust decision turns on. Pure data,
/// so the decision is testable without a socket.</summary>
/// <param name="SystemTrusted">Whether the platform's own validation was satisfied.</param>
public readonly record struct MqttPresentedCertificate(bool SystemTrusted)
{
    /// <summary>What the platform handed the validation callback.</summary>
    public static MqttPresentedCertificate From(X509Certificate? certificate, SslPolicyErrors errors) =>
        new(certificate is not null && errors == SslPolicyErrors.None);
}

/// <summary>Which certificate an encrypted link trusts. A setting rather than a hook, because
/// encryption forced on against a broker with a self-signed certificate cannot connect without
/// one, and the failure otherwise reads as "the connection failed" with no route to a fix.</summary>
/// <remarks>
/// The platform's own stores are the default and prove which machine answered.
/// <see cref="MqttCertificateTrustMode.AcceptAny"/> is the override for a broker whose certificate
/// cannot be made to verify, and it gives that proof up — the traffic stays encrypted against a
/// listener and is open to a far end that substituted itself.
/// </remarks>
public sealed record MqttCertificateTrust
{
    public MqttCertificateTrustMode Mode { get; init; } = MqttCertificateTrustMode.System;

    /// <summary>The platform's own stores decide. The default.</summary>
    public static MqttCertificateTrust SystemTrust { get; } = new();

    /// <summary>Every certificate is accepted.</summary>
    public static MqttCertificateTrust AcceptAny { get; } = new() { Mode = MqttCertificateTrustMode.AcceptAny };

    /// <summary>Whether the presented certificate is one this setting trusts. Pure.</summary>
    public bool Accepts(MqttPresentedCertificate presented) => Mode switch
    {
        // Nothing is read off the certificate, so the platform's verdict does not enter into it.
        MqttCertificateTrustMode.AcceptAny => true,

        _ => presented.SystemTrusted,
    };
}
