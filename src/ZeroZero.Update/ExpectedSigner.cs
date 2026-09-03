using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ZeroZero.Update;

/// <summary>How a certificate compared against the expected signer.</summary>
public enum SignerMatch
{
    Matched,

    /// <summary>The subject is another name.</summary>
    SubjectDiffers,

    /// <summary>The subject is right, the machine does not trust the chain, and the certificate is
    /// not one the application pins — a self-signed certificate spelling the right name.</summary>
    CertificateNotPinned,
}

/// <summary>Who must have signed the installer: the certificate subject, and the thumbprints of the
/// certificates the application accepts when the machine does not trust the chain — which is every
/// machine the studio's self-signed certificate has not been installed on. A subject alone would
/// accept any self-signed certificate that spells the same name.</summary>
public sealed class ExpectedSigner
{
    /// <param name="subject">The subject as .NET renders it — <c>CN=Name, O=Organisation, C=NO</c>.</param>
    /// <param name="certificateThumbprints">SHA-1 (40 hex) or SHA-256 (64 hex) thumbprints; separators
    /// and case are ignored. A certificate about to be rotated in is pinned one release ahead.</param>
    public ExpectedSigner(string subject, IEnumerable<string>? certificateThumbprints = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Subject = NormaliseSubject(subject);

        var pins = new HashSet<string>(StringComparer.Ordinal);
        foreach (string thumbprint in certificateThumbprints ?? [])
            pins.Add(NormaliseThumbprint(thumbprint));
        CertificateThumbprints = pins;
    }

    public string Subject { get; }

    /// <summary>Upper-case hex, no separators.</summary>
    public IReadOnlySet<string> CertificateThumbprints { get; }

    /// <param name="chainTrusted">Whether the machine trusts the chain the certificate sits in. A
    /// trusted chain needs the subject only; an untrusted one needs a pinned thumbprint as well.</param>
    public SignerMatch Match(X509Certificate2 certificate, bool chainTrusted, out string reason)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        string subject = NormaliseSubject(certificate.Subject);
        if (!string.Equals(subject, Subject, StringComparison.Ordinal))
        {
            reason = $"signed by '{subject}', not by '{Subject}'";
            return SignerMatch.SubjectDiffers;
        }

        if (chainTrusted)
        {
            reason = $"signed by '{subject}' under a chain this machine trusts";
            return SignerMatch.Matched;
        }

        string sha1 = certificate.Thumbprint.ToUpperInvariant();
        string sha256 = certificate.GetCertHashString(HashAlgorithmName.SHA256).ToUpperInvariant();
        if (CertificateThumbprints.Contains(sha1) || CertificateThumbprints.Contains(sha256))
        {
            reason = $"signed by '{subject}' with the pinned certificate {sha256}";
            return SignerMatch.Matched;
        }

        reason = CertificateThumbprints.Count == 0
            ? $"signed by '{subject}' under a chain this machine does not trust, and the application pins no certificate; the certificate's SHA-256 thumbprint is {sha256}"
            : $"signed by '{subject}' under a chain this machine does not trust, with a certificate the application does not pin ({sha256})";
        return SignerMatch.CertificateNotPinned;
    }

    /// <summary>One rendering for both sides: the string is encoded and decoded again, so spacing
    /// and escaping differences between the application's text and the certificate's do not count.
    /// <c>Name</c> would echo the string it was built from; only a decode renders.</summary>
    internal static string NormaliseSubject(string subject)
    {
        try
        {
            return new X500DistinguishedName(subject).Decode(X500DistinguishedNameFlags.Reversed);
        }
        catch (CryptographicException ex)
        {
            throw new ArgumentException($"'{subject}' is not a distinguished name.", nameof(subject), ex);
        }
    }

    internal static string NormaliseThumbprint(string thumbprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);

        string hex = string.Concat(thumbprint.Where(c => !char.IsWhiteSpace(c) && c != ':' && c != '-')).ToUpperInvariant();
        if ((hex.Length != 40 && hex.Length != 64) || !hex.All(char.IsAsciiHexDigit))
            throw new ArgumentException($"'{thumbprint}' is not a SHA-1 (40 hex) or SHA-256 (64 hex) thumbprint.", nameof(thumbprint));
        return hex;
    }
}
