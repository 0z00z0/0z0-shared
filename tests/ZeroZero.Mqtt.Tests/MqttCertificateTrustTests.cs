using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>Which certificate an encrypted link accepts. Pure, so the decision is pinned without a
/// handshake. Only accepting any certificate takes one the platform rejects.</summary>
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

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void SystemTrust_TakesThePlatformsAnswerAndNothingElse()
    {
        using var certificate = SelfSigned("broker.invalid");

        Assert.True(MqttCertificateTrust.SystemTrust.Accepts(Presented(certificate, systemTrusted: true)));
        Assert.False(MqttCertificateTrust.SystemTrust.Accepts(Presented(certificate, systemTrusted: false)));
    }

    [Fact]
    public void AcceptingAnyCertificate_TakesOneThePlatformRejects()
    {
        using var certificate = SelfSigned("broker.invalid");

        Assert.True(MqttCertificateTrust.AcceptAny.Accepts(Presented(certificate, systemTrusted: false)));
    }
}
