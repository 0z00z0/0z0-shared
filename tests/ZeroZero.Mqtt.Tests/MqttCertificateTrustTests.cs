using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>Which certificate an encrypted link accepts. Pure, so the decision is pinned without a
/// handshake — and pinning must be exact, because a trust setting that accepts anything is
/// encryption against a passive listener and nothing against an active one.</summary>
public class MqttCertificateTrustTests
{
    private static X509Certificate2 SelfSigned(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static MqttPresentedCertificate Presented(X509Certificate2 certificate, bool systemTrusted) =>
        MqttPresentedCertificate.From(
            certificate, systemTrusted ? SslPolicyErrors.None : SslPolicyErrors.RemoteCertificateChainErrors);

    [Fact]
    public void SystemTrust_IsTheDefault() =>
        Assert.Equal(MqttCertificateTrustMode.System, new MqttCertificateTrust().Mode);

    [Fact]
    public void SystemTrust_TakesThePlatformsAnswerAndNothingElse()
    {
        using var certificate = SelfSigned("broker.invalid");

        Assert.True(MqttCertificateTrust.SystemTrust.Accepts(Presented(certificate, systemTrusted: true)));
        Assert.False(MqttCertificateTrust.SystemTrust.Accepts(Presented(certificate, systemTrusted: false)));
    }

    [Fact]
    public void APinnedThumbprint_AcceptsThatCertificateThoughThePlatformWouldNot()
    {
        using var certificate = SelfSigned("broker.invalid");
        var trust = MqttCertificateTrust.ForThumbprint(certificate.Thumbprint);

        Assert.True(trust.Accepts(Presented(certificate, systemTrusted: false)));
    }

    [Fact]
    public void APinnedThumbprint_RefusesEveryOtherCertificate()
    {
        using var pinned = SelfSigned("broker.invalid");
        using var other = SelfSigned("broker.invalid");
        var trust = MqttCertificateTrust.ForThumbprint(pinned.Thumbprint);

        Assert.False(trust.Accepts(Presented(other, systemTrusted: true)));
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("{0} ")]
    public void APinnedThumbprint_IgnoresTheSpacingAndCaseAViewerRenders(string format)
    {
        using var certificate = SelfSigned("broker.invalid");
        string typed = string.Format(format, Spaced(certificate.Thumbprint.ToLowerInvariant()));

        Assert.True(MqttCertificateTrust.ForThumbprint(typed).Accepts(Presented(certificate, false)));

        static string Spaced(string hex) =>
            string.Join(' ', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
    }

    [Fact]
    public void APinnedCertificate_MatchesByteForByte()
    {
        using var pinned = SelfSigned("broker.invalid");
        using var other = SelfSigned("broker.invalid");
        var trust = MqttCertificateTrust.ForCertificate(pinned);

        Assert.True(trust.Accepts(Presented(pinned, systemTrusted: false)));
        Assert.False(trust.Accepts(Presented(other, systemTrusted: true)));
    }

    [Fact]
    public void AnUnusableThumbprintRefusesRatherThanFallingBackToThePlatform()
    {
        using var certificate = SelfSigned("broker.invalid");
        var trust = MqttCertificateTrust.ForThumbprint("   ");

        Assert.NotNull(trust.Validate());
        Assert.False(trust.Accepts(Presented(certificate, systemTrusted: true)));
    }

    [Fact]
    public void AnUnusableCertificateRefusesRatherThanFallingBackToThePlatform()
    {
        using var certificate = SelfSigned("broker.invalid");
        var trust = MqttCertificateTrust.ForCertificate("not base64 at all!!");

        Assert.NotNull(trust.Validate());
        Assert.False(trust.Accepts(Presented(certificate, systemTrusted: true)));
    }

    [Fact]
    public void Validate_PassesForASettingThatCanBeApplied()
    {
        using var certificate = SelfSigned("broker.invalid");

        Assert.Null(MqttCertificateTrust.SystemTrust.Validate());
        Assert.Null(MqttCertificateTrust.ForThumbprint(certificate.Thumbprint).Validate());
        Assert.Null(MqttCertificateTrust.ForCertificate(certificate).Validate());
    }
}
