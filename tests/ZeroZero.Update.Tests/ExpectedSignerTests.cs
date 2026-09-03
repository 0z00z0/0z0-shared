using Xunit;

namespace ZeroZero.Update.Tests;

public class ExpectedSignerTests(SignedFileFactory files) : IClassFixture<SignedFileFactory>
{
    [Theory]
    [InlineData("ad26d1a44e4d772cedb730988e645fd127f7c0300678f9bd1c09c411443fe084")]
    [InlineData("AD:26:D1:A4:4E:4D:77:2C:ED:B7:30:98:8E:64:5F:D1:27:F7:C0:30:06:78:F9:BD:1C:09:C4:11:44:3F:E0:84")]
    [InlineData(" ad26 d1a4 4e4d 772c edb7 3098 8e64 5fd1 27f7 c030 0678 f9bd 1c09 c411 443f e084 ")]
    public void Thumbprints_AreNormalisedToUpperHex(string given)
    {
        var signer = new ExpectedSigner("CN=Test", [given]);

        Assert.Contains("AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084", signer.CertificateThumbprints);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE08")]
    [InlineData("GG26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084")]
    public void Thumbprints_ThatAreNotSha1OrSha256AreRefused(string given)
    {
        Assert.Throws<ArgumentException>(() => new ExpectedSigner("CN=Test", [given]));
    }

    [Fact]
    public void Subject_IsRenderedOneWay()
    {
        var signer = new ExpectedSigner("CN=ZeroZero Test Signer,O=ZeroZero Software Tests,C=NO");

        Assert.Equal(SignedFileFactory.ExpectedSubject, signer.Subject);
        // And the certificate's own rendering is the same rendering.
        Assert.Equal(files.Expected.Subject, signer.Subject);
        Assert.Equal(files.Expected.Subject, ExpectedSigner.NormaliseSubject(files.Expected.Subject));
    }

    [Fact]
    public void Subject_ThatIsNotADistinguishedNameIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ExpectedSigner("ZeroZero Software"));
    }

    [Fact]
    public void Match_AcceptsTheSubjectAloneUnderATrustedChain()
    {
        SignerMatch match = files.SignerUnpinned.Match(files.Expected, chainTrusted: true, out string reason);

        Assert.Equal(SignerMatch.Matched, match);
        Assert.Contains("trusts", reason);
    }

    [Fact]
    public void Match_NeedsAPinUnderAnUntrustedChain()
    {
        Assert.Equal(SignerMatch.CertificateNotPinned, files.SignerUnpinned.Match(files.Expected, chainTrusted: false, out _));
        Assert.Equal(SignerMatch.Matched, files.Signer.Match(files.Expected, chainTrusted: false, out _));
    }

    [Fact]
    public void Match_RefusesAnotherSubjectWhateverTheChain()
    {
        Assert.Equal(SignerMatch.SubjectDiffers, files.Signer.Match(files.Other, chainTrusted: true, out _));
        Assert.Equal(SignerMatch.SubjectDiffers, files.Signer.Match(files.Other, chainTrusted: false, out _));
    }

    [Fact]
    public void Match_RefusesTheRightNameOnAnotherKey()
    {
        SignerMatch match = files.Signer.Match(files.Impostor, chainTrusted: false, out string reason);

        Assert.Equal(SignerMatch.CertificateNotPinned, match);
        Assert.Contains(files.Impostor.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), reason);
    }
}
