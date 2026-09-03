using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace ZeroZero.Update.Tests;

/// <summary>The verifier against real files: signed here by certificates made here, and the bad
/// forms of each. Every refusal is a verdict, and the one acceptance carries the untrusted-root
/// code a self-signed certificate yields — the studio certificate's own shape on a machine that
/// has not installed it.</summary>
public class InstallerVerifierTests(SignedFileFactory files) : IClassFixture<SignedFileFactory>
{
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);

    [Fact]
    public void Verify_AcceptsTheExpectedSignerWithThePublishedHash()
    {
        VerificationResult result = InstallerVerifier.Verify(files.SignedByExpectedPath, files.Sha256(files.SignedByExpectedPath), files.Signer);

        Assert.Equal(VerificationVerdict.Verified, result.Verdict);
        Assert.True(result.IsVerified);
        Assert.Equal(CERT_E_UNTRUSTEDROOT, result.TrustResult);
        Assert.Equal(SignedFileFactory.ExpectedSubject, result.SignerSubject);
        Assert.Contains("pinned certificate", result.Detail);
    }

    [Fact]
    public void Verify_RefusesAWrongHash()
    {
        VerificationResult result = InstallerVerifier.Verify(files.SignedByExpectedPath, files.Sha256(files.SignedByOtherPath), files.Signer);

        Assert.Equal(VerificationVerdict.HashMismatch, result.Verdict);
        Assert.False(result.IsVerified);
        Assert.Equal(files.Sha256(files.SignedByExpectedPath), result.ActualSha256);
        // The signature is not looked at once the hash has failed.
        Assert.Null(result.TrustResult);
    }

    [Fact]
    public void Verify_RefusesAFileSignedBySomeoneElse()
    {
        VerificationResult result = InstallerVerifier.Verify(files.SignedByOtherPath, files.Sha256(files.SignedByOtherPath), files.Signer);

        Assert.Equal(VerificationVerdict.SignerMismatch, result.Verdict);
        Assert.False(result.IsVerified);
        Assert.Contains("Somebody Else", result.Detail);
        Assert.Equal(SignedFileFactory.OtherSubject, result.SignerSubject);
    }

    [Fact]
    public void Verify_RefusesAnUnsignedFile()
    {
        VerificationResult result = InstallerVerifier.Verify(files.UnsignedPath, files.Sha256(files.UnsignedPath), files.Signer);

        Assert.Equal(VerificationVerdict.NotSigned, result.Verdict);
        Assert.False(result.IsVerified);
        Assert.Equal(TRUST_E_NOSIGNATURE, result.TrustResult);
    }

    [Fact]
    public void Verify_RefusesATruncatedFileAgainstThePublishedHash()
    {
        VerificationResult result = InstallerVerifier.Verify(files.TruncatedPath, files.Sha256(files.SignedByExpectedPath), files.Signer);

        Assert.Equal(VerificationVerdict.HashMismatch, result.Verdict);
        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Verify_RefusesATruncatedFileEvenAgainstItsOwnHash()
    {
        // The hash check passed by construction; the signature check alone stands between a
        // file cut short and the launcher.
        VerificationResult result = InstallerVerifier.Verify(files.TruncatedPath, files.Sha256(files.TruncatedPath), files.Signer);

        Assert.False(result.IsVerified);
        Assert.Contains(result.Verdict, new[] { VerificationVerdict.NotSigned, VerificationVerdict.SignatureInvalid, VerificationVerdict.SignatureCheckFailed });
    }

    [Fact]
    public void Verify_RefusesATamperedFileEvenAgainstItsOwnHash()
    {
        // A hash published beside a substituted file matches the substituted file: only the
        // signature says the bytes are not the ones that were signed.
        VerificationResult result = InstallerVerifier.Verify(files.TamperedPath, files.Sha256(files.TamperedPath), files.Signer);

        Assert.Equal(VerificationVerdict.SignatureInvalid, result.Verdict);
        Assert.False(result.IsVerified);
        Assert.Equal(TRUST_E_BAD_DIGEST, result.TrustResult);
    }

    [Fact]
    public void Verify_RefusesAnImpostorSpellingTheExpectedNameWithAnUnpinnedCertificate()
    {
        VerificationResult result = InstallerVerifier.Verify(files.SignedByImpostorPath, files.Sha256(files.SignedByImpostorPath), files.Signer);

        Assert.Equal(VerificationVerdict.CertificateNotPinned, result.Verdict);
        Assert.False(result.IsVerified);
        Assert.Equal(SignedFileFactory.ExpectedSubject, result.SignerSubject);
        Assert.Contains("does not pin", result.Detail);
    }

    [Fact]
    public void Verify_RefusesTheExpectedSignerWhenNoCertificateIsPinned()
    {
        // A subject-only expectation accepts nothing self-signed: the name is not the proof.
        VerificationResult result = InstallerVerifier.Verify(files.SignedByExpectedPath, files.Sha256(files.SignedByExpectedPath), files.SignerUnpinned);

        Assert.Equal(VerificationVerdict.CertificateNotPinned, result.Verdict);
        Assert.False(result.IsVerified);
        Assert.Contains("pins no certificate", result.Detail);
    }

    [Fact]
    public void Verify_AcceptsASha1Pin()
    {
        VerificationResult result = InstallerVerifier.Verify(files.SignedByExpectedPath, files.Sha256(files.SignedByExpectedPath), files.SignerBySha1);

        Assert.Equal(VerificationVerdict.Verified, result.Verdict);
    }

    [Fact]
    public void Verify_AcceptsThePublishedHashInAnyCase()
    {
        string hash = files.Sha256(files.SignedByExpectedPath).ToLowerInvariant();

        VerificationResult result = InstallerVerifier.Verify(files.SignedByExpectedPath, "  " + hash + " ", files.Signer);

        Assert.Equal(VerificationVerdict.Verified, result.Verdict);
    }

    [Fact]
    public void Verify_ReportsAMissingFile()
    {
        VerificationResult result = InstallerVerifier.Verify(Path.Combine(files.Root, "absent.exe"), files.Sha256(files.SignedByExpectedPath), files.Signer);

        Assert.Equal(VerificationVerdict.FileMissing, result.Verdict);
        Assert.False(result.IsVerified);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084")]
    public void Verify_RefusesAPublishedHashThatIsNotASha256(string hash)
    {
        Assert.Throws<ArgumentException>(() => InstallerVerifier.Verify(files.SignedByExpectedPath, hash, files.Signer));
    }

    [TrustedRuntimeFact]
    public void Verify_AcceptsATrustedChainBySubjectAlone()
    {
        string runtime = typeof(object).Assembly.Location;
        using X509Certificate2 signer = InstallerVerifier.ReadSignerCertificate(runtime);

        VerificationResult result = InstallerVerifier.Verify(runtime, InstallerVerifier.Sha256Of(runtime), new ExpectedSigner(signer.Subject));

        Assert.Equal(VerificationVerdict.Verified, result.Verdict);
        Assert.Equal(0, result.TrustResult);
        Assert.Contains("trusts", result.Detail);
    }

    [TrustedRuntimeFact]
    public void Verify_RefusesATrustedChainUnderAnotherSubject()
    {
        string runtime = typeof(object).Assembly.Location;

        VerificationResult result = InstallerVerifier.Verify(runtime, InstallerVerifier.Sha256Of(runtime), files.Signer);

        Assert.Equal(VerificationVerdict.SignerMismatch, result.Verdict);
        Assert.Equal(0, result.TrustResult);
    }
}
