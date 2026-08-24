using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>What the connection is applied with, and what it deliberately cannot see. Two
/// projections built from equal settings must be equal, because that is what makes "apply on every
/// settings change" safe: a group toggle and a remembered endpoint both have to leave it
/// untouched.</summary>
public class MqttConnectParametersTests
{
    private static MqttSettings Settings() => new()
    {
        Enabled = true,
        Host = "broker.invalid",
        Port = 1883,
        Username = "user",
        Password = "first-placeholder",
        DeviceId = "desk01",
        Groups = { ["diagnostics"] = false },
    };

    [Fact]
    public void AGroupToggleLeavesTheProjectionIdentical()
    {
        var before = Settings();
        var after = Settings();
        after.Groups["diagnostics"] = true;
        after.Groups["metrics"] = false;

        Assert.Equal(before.Connect(), after.Connect());
    }

    [Fact]
    public void TheProjectionHasNowhereToPutAnEndpointMemory()
    {
        // Persisting it as a setting is what turns a successful connect into a settings change, and
        // a consumer that re-applies on a settings change then reconnects on its own success.
        var properties = typeof(MqttConnectParameters).GetProperties().Select(p => p.PropertyType);
        var stored = typeof(MqttSettings).GetProperties().Select(p => p.PropertyType);

        Assert.DoesNotContain(typeof(MqttEndpointMemory), properties);
        Assert.DoesNotContain(typeof(MqttEndpointMemory), stored);
    }

    [Fact]
    public void TheProjectionCarriesNoPasswordAsAValue()
    {
        var parameters = Settings().Connect();

        Assert.DoesNotContain("first-placeholder", parameters.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("first-placeholder", parameters.CredentialRef, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePasswordIsStillReachableForTheHandshake() =>
        Assert.Equal("first-placeholder", Settings().Connect().Password());

    [Fact]
    public void AChangedPasswordIsAChangedConnection()
    {
        var before = Settings();
        var after = Settings();
        after.Password = "second-placeholder";

        Assert.NotEqual(before.Connect(), after.Connect());
    }

    [Fact]
    public void TwoDifferentPasswordsDoNotShareAFingerprint()
    {
        Assert.NotEqual(MqttConnectParameters.Fingerprint("first-placeholder"),
                        MqttConnectParameters.Fingerprint("second-placeholder"));
        Assert.Equal("", MqttConnectParameters.Fingerprint(""));
    }

    [Fact]
    public void TwoProjectionsOfTheSameSettingsAreInterchangeable()
    {
        var settings = Settings();

        Assert.Equal(settings.Connect(), settings.Connect());
        Assert.Equal(settings.Connect().GetHashCode(), settings.Connect().GetHashCode());
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Port")]
    [InlineData("Username")]
    [InlineData("DeviceId")]
    [InlineData("DeviceName")]
    [InlineData("DiscoveryPrefix")]
    [InlineData("Enabled")]
    [InlineData("TransportMode")]
    [InlineData("EncryptionMode")]
    [InlineData("CertificateTrust")]
    public void EveryBrokerSettingIsAChangedConnection(string property)
    {
        var before = Settings();
        var after = Settings();
        Mutate(after, property);

        Assert.NotEqual(before.Connect(), after.Connect());
    }

    private static void Mutate(MqttSettings settings, string property)
    {
        switch (property)
        {
            case "Host": settings.Host = "other.invalid"; break;
            case "Port": settings.Port = 8883; break;
            case "Username": settings.Username = "someone"; break;
            case "DeviceId": settings.DeviceId = "workshop"; break;
            case "DeviceName": settings.DeviceName = "Workshop"; break;
            case "DiscoveryPrefix": settings.DiscoveryPrefix = "elsewhere"; break;
            case "Enabled": settings.Enabled = false; break;
            case "TransportMode": settings.TransportMode = MqttTransportMode.WebSocket; break;
            case "EncryptionMode": settings.EncryptionMode = MqttEncryptionMode.On; break;
            case "CertificateTrust": settings.CertificateTrust = MqttCertificateTrust.ForThumbprint("ABCD"); break;
            default: throw new ArgumentOutOfRangeException(nameof(property), property, "unknown setting");
        }
    }

    [Fact]
    public void ShouldRun_NeedsBothTheSwitchAndAHost()
    {
        var settings = Settings();
        Assert.True(settings.Connect().ShouldRun);

        settings.Host = "  ";
        Assert.False(settings.Connect().ShouldRun);
    }

    [Fact]
    public void Copy_HandsBackSomethingAWriterCannotUseToReachTheOriginal()
    {
        var original = Settings();

        var copy = original.Copy();
        copy.Host = "other.invalid";
        copy.Groups["diagnostics"] = true;

        Assert.Equal("broker.invalid", original.Host);
        Assert.False(original.Groups["diagnostics"]);
    }
}
