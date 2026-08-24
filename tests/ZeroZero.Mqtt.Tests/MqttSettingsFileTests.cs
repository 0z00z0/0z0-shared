using Xunit;
using ZeroZero.Config;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The ready-made store: the module owns the file name, the host owns the directory. A host
/// whose configuration is one document implements the same three members over that document and
/// never constructs this.</summary>
public class MqttSettingsFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "zerozero-mqtt-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void In_PutsTheModulesFileNameInTheHostsDirectory()
    {
        using var store = MqttSettingsFile.In(_directory);

        Assert.Equal(Path.Combine(_directory, MqttSettingsFile.DefaultFileName), store.FilePath);
    }

    [Fact]
    public void AMissingFileReadsAsDefaultsWithPublishingOff()
    {
        using var store = MqttSettingsFile.In(_directory);

        var settings = store.Read();

        Assert.False(settings.Enabled);
        Assert.Equal("", settings.Host);
        Assert.Equal(MqttSettings.DefaultDiscoveryPrefix, settings.DiscoveryPrefix);
        Assert.Equal(MqttCertificateTrustMode.System, settings.CertificateTrust.Mode);
    }

    [Fact]
    public void EverySettingSurvivesARoundTrip()
    {
        using (var store = MqttSettingsFile.In(_directory))
        {
            store.Update(s =>
            {
                s.Enabled = true;
                s.Host = "broker.invalid";
                s.Port = 8883;
                s.TransportMode = MqttTransportMode.WebSocket;
                s.EncryptionMode = MqttEncryptionMode.On;
                s.CertificateTrust = MqttCertificateTrust.ForThumbprint("AA BB CC");
                s.Username = "user";
                s.DeviceId = "desk01";
                s.DeviceName = "Desk";
                s.DiscoveryPrefix = "elsewhere";
                s.Groups["metrics"] = false;
            });
        }

        using var reopened = MqttSettingsFile.In(_directory);
        var settings = reopened.Read();

        Assert.True(settings.Enabled);
        Assert.Equal(8883, settings.Port);
        Assert.Equal(MqttTransportMode.WebSocket, settings.TransportMode);
        Assert.Equal(MqttEncryptionMode.On, settings.EncryptionMode);
        Assert.Equal(MqttCertificateTrustMode.Thumbprint, settings.CertificateTrust.Mode);
        Assert.Equal("elsewhere", settings.DiscoveryPrefix);
        Assert.False(settings.Groups["metrics"]);
    }

    [Fact]
    public void AnEnumIsPersistedAsItsDeclaredName()
    {
        using var store = MqttSettingsFile.In(_directory);
        store.Update(s => s.TransportMode = MqttTransportMode.WebSocket);

        string text = File.ReadAllText(store.FilePath);

        Assert.Contains("\"WebSocket\"", text);
    }

    [Fact]
    public void AWriteRaisesTheChangeEventOnce()
    {
        using var store = MqttSettingsFile.In(_directory);
        int changes = 0;
        store.Changed += () => changes++;

        store.Update(s => s.Host = "broker.invalid");

        Assert.Equal(1, changes);
    }

    [Fact]
    public void AWriteThatChangesNothingRaisesNothing()
    {
        using var store = MqttSettingsFile.In(_directory);
        store.Update(s => s.Host = "broker.invalid");
        int changes = 0;
        store.Changed += () => changes++;

        store.Update(s => s.Host = "broker.invalid");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Update_MutatesTheLiveDocumentRatherThanACallersSnapshot()
    {
        // A settings panel holds a snapshot from when it opened; committing one field must not roll
        // back whatever a sibling changed meanwhile.
        using var store = MqttSettingsFile.In(_directory);
        var stale = store.Read();
        store.Update(s => s.Groups["metrics"] = true);

        store.Update(s => s.Host = stale.Host + "broker.invalid");

        var settings = store.Read();
        Assert.Equal("broker.invalid", settings.Host);
        Assert.True(settings.Groups["metrics"]);
    }

    [Fact]
    public void Read_HandsBackASnapshotMutatingItChangesNothing()
    {
        using var store = MqttSettingsFile.In(_directory);
        store.Update(s => s.Host = "broker.invalid");

        store.Read().Host = "other.invalid";

        Assert.Equal("broker.invalid", store.Read().Host);
    }

    [Fact]
    public void AHostMayNameTheFileItselfWhenItsLayoutSaysSo()
    {
        using var store = new MqttSettingsFile(new SettingsFileOptions(_directory, "broker.json"));

        Assert.EndsWith("broker.json", store.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedFileIsPreservedBeforeDefaultsTakeOver()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, MqttSettingsFile.DefaultFileName);
        File.WriteAllText(path, "{ this is not json");

        using var store = MqttSettingsFile.In(_directory);

        Assert.False(store.Read().Enabled);
        Assert.NotNull(store.File.LastQuarantinePath);
    }
}
