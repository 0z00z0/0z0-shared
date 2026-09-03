using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ZeroZero.Update;

public enum VerificationVerdict
{
    /// <summary>The file hashes to the published SHA-256 and carries a valid signature by the
    /// expected signer. The only verdict that runs.</summary>
    Verified,

    FileMissing,

    /// <summary>The bytes are not the bytes the release published: truncated, corrupted, or another
    /// file. Checked first, before the signature is looked at.</summary>
    HashMismatch,

    /// <summary>No embedded signature.</summary>
    NotSigned,

    /// <summary>A signature is there and the file no longer matches it.</summary>
    SignatureInvalid,

    /// <summary>Signed by another subject.</summary>
    SignerMismatch,

    /// <summary>Signed by the expected subject under a chain this machine does not trust, with a
    /// certificate the application does not pin.</summary>
    CertificateNotPinned,

    /// <summary>Windows reported something else — an expired certificate, a broken chain, a file it
    /// could not read. The code is in the result.</summary>
    SignatureCheckFailed,
}

/// <param name="Detail">Why, in a sentence a dialog can show.</param>
/// <param name="TrustResult">WinVerifyTrust's HRESULT, where the check got that far.</param>
public sealed record VerificationResult(
    VerificationVerdict Verdict,
    string Detail,
    string? ActualSha256 = null,
    string? SignerSubject = null,
    int? TrustResult = null)
{
    public bool IsVerified => Verdict == VerificationVerdict.Verified;
}

/// <summary>Whether a downloaded file is the release it claims to be. Two checks, in this order,
/// answering different questions: the SHA-256 against the hash the release publishes says whether
/// the download is whole; the signature and its publisher against the expected signer say whether
/// it is the publisher's. A file that fails either does not run.</summary>
public static class InstallerVerifier
{
    public static VerificationResult Verify(string path, string expectedSha256Hex, ExpectedSigner signer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(signer);
        string expected = NormaliseSha256(expectedSha256Hex);

        if (!File.Exists(path))
            return new VerificationResult(VerificationVerdict.FileMissing, $"there is no file at {path}");

        string actual = Sha256Of(path);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            return new VerificationResult(VerificationVerdict.HashMismatch,
                $"the file hashes to {actual}; the release publishes {expected}", ActualSha256: actual);

        int trust = AuthenticodeSignature.Check(path);
        bool chainTrusted;
        switch (trust)
        {
            case NativeMethods.S_OK:
                chainTrusted = true;
                break;
            case NativeMethods.CERT_E_UNTRUSTEDROOT:
                chainTrusted = false;
                break;
            case NativeMethods.TRUST_E_NOSIGNATURE:
            case NativeMethods.TRUST_E_SUBJECT_FORM_UNKNOWN:
            case NativeMethods.TRUST_E_PROVIDER_UNKNOWN:
                return new VerificationResult(VerificationVerdict.NotSigned, "the file carries no signature", actual, TrustResult: trust);
            case NativeMethods.TRUST_E_BAD_DIGEST:
                return new VerificationResult(VerificationVerdict.SignatureInvalid, "the file has been altered since it was signed", actual, TrustResult: trust);
            default:
                return new VerificationResult(VerificationVerdict.SignatureCheckFailed,
                    $"Windows refused the signature with {Describe(trust)}", actual, TrustResult: trust);
        }

        X509Certificate2 certificate;
        try
        {
            certificate = ReadSignerCertificate(path);
        }
        catch (CryptographicException ex)
        {
            return new VerificationResult(VerificationVerdict.SignatureCheckFailed,
                $"the signer certificate could not be read: {ex.Message}", actual, TrustResult: trust);
        }

        using (certificate)
        {
            SignerMatch match = signer.Match(certificate, chainTrusted, out string reason);
            VerificationVerdict verdict = match switch
            {
                SignerMatch.Matched => VerificationVerdict.Verified,
                SignerMatch.SubjectDiffers => VerificationVerdict.SignerMismatch,
                _ => VerificationVerdict.CertificateNotPinned,
            };
            return new VerificationResult(verdict, reason, actual, certificate.Subject, trust);
        }
    }

    public static string Sha256Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>The certificate of the file's primary signer — the signature WinVerifyTrust
    /// judged. Only the signer's identity is read here; whether the signature holds was settled
    /// before this is called.</summary>
    /// <exception cref="CryptographicException">The file carries no readable signer.</exception>
    internal static X509Certificate2 ReadSignerCertificate(string path)
    {
        // SYSLIB0057 points at X509CertificateLoader, which loads certificate blobs and has no
        // reader for the signer embedded in an Authenticode-signed file; this is that reader.
#pragma warning disable SYSLIB0057
        using X509Certificate raw = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
        return new X509Certificate2(raw);
    }

    internal static string NormaliseSha256(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        string upper = hex.Trim().ToUpperInvariant();
        if (upper.Length != 64 || !upper.All(char.IsAsciiHexDigit))
            throw new ArgumentException($"'{hex}' is not a SHA-256 (64 hex characters).", nameof(hex));
        return upper;
    }

    private static string Describe(int hresult)
    {
        string code = "0x" + hresult.ToString("X8", CultureInfo.InvariantCulture);
        return hresult switch
        {
            NativeMethods.CERT_E_EXPIRED => $"{code} (the signing certificate has expired and the signature carries no timestamp)",
            NativeMethods.CERT_E_CHAINING => $"{code} (the certificate chain could not be built)",
            NativeMethods.CRYPT_E_FILE_ERROR => $"{code} (the file could not be read)",
            _ => code,
        };
    }
}
