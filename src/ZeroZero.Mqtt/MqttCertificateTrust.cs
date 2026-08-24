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

    /// <summary>One named certificate, matched on its SHA-1 thumbprint.</summary>
    Thumbprint,

    /// <summary>One named certificate, matched byte for byte.</summary>
    Certificate,
}

/// <summary>What a broker presented, reduced to the three facts a trust decision turns on. Pure
/// data, so the decision is testable without a socket.</summary>
/// <param name="Thumbprint">The certificate's SHA-1 thumbprint, in any spacing or case.</param>
/// <param name="RawData">The certificate's DER encoding.</param>
/// <param name="SystemTrusted">Whether the platform's own validation was satisfied.</param>
public readonly record struct MqttPresentedCertificate(
    string Thumbprint, ReadOnlyMemory<byte> RawData, bool SystemTrusted)
{
    /// <summary>What the platform handed the validation callback.</summary>
    public static MqttPresentedCertificate From(X509Certificate? certificate, SslPolicyErrors errors) =>
        new(certificate?.GetCertHashString() ?? "",
            certificate?.GetRawCertData() ?? [],
            errors == SslPolicyErrors.None);
}

/// <summary>Which certificate an encrypted link trusts. A setting rather than a hook, because
/// encryption forced on against a broker with a self-signed certificate cannot connect without
/// one, and the failure otherwise reads as "the connection failed" with no route to a fix.</summary>
/// <remarks>
/// Pinning is deliberately exact rather than a blanket "accept anything": a link that accepts every
/// certificate is encrypted against a passive listener and open to an active one, which is the
/// failure mode the setting exists to close.
/// </remarks>
public sealed record MqttCertificateTrust
{
    public MqttCertificateTrustMode Mode { get; init; } = MqttCertificateTrustMode.System;

    /// <summary>The SHA-1 thumbprint to accept under <see cref="MqttCertificateTrustMode.Thumbprint"/>.
    /// Spacing, separators and case are ignored, so a value copied out of a certificate viewer works
    /// as pasted.</summary>
    public string Thumbprint { get; init; } = "";

    /// <summary>The base64 DER encoding of the certificate to accept under
    /// <see cref="MqttCertificateTrustMode.Certificate"/>.</summary>
    public string Certificate { get; init; } = "";

    /// <summary>The platform's own stores decide. The default, and the only mode that needs no
    /// value alongside it.</summary>
    public static MqttCertificateTrust SystemTrust { get; } = new();

    public static MqttCertificateTrust ForThumbprint(string thumbprint) =>
        new() { Mode = MqttCertificateTrustMode.Thumbprint, Thumbprint = thumbprint };

    public static MqttCertificateTrust ForCertificate(string base64) =>
        new() { Mode = MqttCertificateTrustMode.Certificate, Certificate = base64 };

    public static MqttCertificateTrust ForCertificate(X509Certificate certificate) =>
        ForCertificate(Convert.ToBase64String(certificate.GetRawCertData()));

    /// <summary>Why this trust setting cannot be applied, or null when it can. A mode naming a
    /// certificate with nothing to name it by would silently fall back to something — refusing early
    /// is what keeps that from being a downgrade nobody chose.</summary>
    public string? Validate() => Mode switch
    {
        MqttCertificateTrustMode.Thumbprint when NormaliseThumbprint(Thumbprint).Length == 0 =>
            "A thumbprint is needed to pin a certificate.",
        MqttCertificateTrustMode.Certificate when DecodeCertificate() is null =>
            "The pinned certificate is missing or is not valid base64.",
        _ => null,
    };

    /// <summary>Whether the presented certificate is the one this setting trusts. Pure.</summary>
    /// <remarks>An unusable pin refuses rather than falling through to platform validation: the
    /// point of pinning is that the platform's answer is not the one being asked for.</remarks>
    public bool Accepts(MqttPresentedCertificate presented) => Mode switch
    {
        MqttCertificateTrustMode.Thumbprint =>
            NormaliseThumbprint(Thumbprint) is { Length: > 0 } wanted
            && string.Equals(wanted, NormaliseThumbprint(presented.Thumbprint), StringComparison.Ordinal),

        MqttCertificateTrustMode.Certificate =>
            DecodeCertificate() is { } wanted && wanted.AsSpan().SequenceEqual(presented.RawData.Span),

        _ => presented.SystemTrusted,
    };

    private byte[]? DecodeCertificate()
    {
        if (Certificate.Length == 0) return null;

        // Base64 never decodes to more bytes than it took to write, so one buffer of that size fits.
        var buffer = new byte[Certificate.Length];
        return Convert.TryFromBase64String(Certificate, buffer, out int written) && written > 0
            ? buffer[..written]
            : null;
    }

    // Viewers render a thumbprint with spaces, colons or neither, and in either case. Only the hex
    // digits carry meaning, so everything else is dropped before the comparison.
    private static string NormaliseThumbprint(string raw)
    {
        Span<char> buffer = stackalloc char[raw.Length];
        int length = 0;
        foreach (char c in raw)
            if (char.IsAsciiHexDigit(c)) buffer[length++] = char.ToUpperInvariant(c);
        return new string(buffer[..length]);
    }
}
