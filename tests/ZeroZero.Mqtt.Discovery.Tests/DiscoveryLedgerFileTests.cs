using Xunit;
using ZeroZero.Config;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The record on disk. What makes eviction survive a restart is that a second process reads
/// back what the first one wrote, so that round trip is asserted rather than assumed.</summary>
public class DiscoveryLedgerFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "zz-discovery-" + Guid.NewGuid().ToString("N"));

    public DiscoveryLedgerFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheModuleOwnsTheNameAndTheHostOwnsTheDirectory()
    {
        var ledger = DiscoveryLedgerFile.In(_directory);

        Assert.Equal(
            Path.Combine(_directory, DiscoveryLedgerFile.DefaultFileName), ledger.FilePath);
    }

    [Fact]
    public void AFreshDirectoryHasNothingRecorded() =>
        Assert.Empty(DiscoveryLedgerFile.In(_directory).Read().Devices);

    [Fact]
    public void WhatOneRunWroteTheNextRunReads()
    {
        DiscoveryLedgerFile.In(_directory).Update(ledger => ledger.Devices.Add(new PublishedDevice
        {
            DeviceId = Sample.DeviceId,
            ConfigTopic = Sample.ConfigTopic,
            AvailabilityTopic = Sample.Availability,
            Entities =
            [
                new PublishedEntity
                {
                    EntityId = "vm_alpha", Platform = "sensor", StateTopic = Sample.State("vm_alpha"),
                },
            ],
        }));

        var reopened = DiscoveryLedgerFile.In(_directory).Read().Find(Sample.DeviceId);

        Assert.NotNull(reopened);
        Assert.Equal(Sample.Availability, reopened.AvailabilityTopic);
        Assert.Equal("vm_alpha", reopened.Entities.Single().EntityId);
        Assert.Equal(Sample.State("vm_alpha"), reopened.Entities.Single().StateTopic);
    }

    [Fact]
    public void AnEmptiedValueTopicIsReadBackAsAThingAlreadyDone()
    {
        // Without the round trip it is emptied again on every start, against a key a consumer may
        // since have started using again.
        DiscoveryLedgerFile.In(_directory).Update(ledger => ledger.Devices.Add(new PublishedDevice
        {
            DeviceId = Sample.DeviceId,
            ConfigTopic = Sample.ConfigTopic,
            RetiredChannels = [Sample.State("legacy_state")],
        }));

        var reopened = DiscoveryLedgerFile.In(_directory).Read().Find(Sample.DeviceId);

        Assert.NotNull(reopened);
        Assert.Equal([Sample.State("legacy_state")], reopened.RetiredChannels);
    }

    [Fact]
    public void ReadHandsBackASnapshot()
    {
        var store = DiscoveryLedgerFile.In(_directory);
        store.Update(ledger => ledger.Devices.Add(new PublishedDevice { ConfigTopic = Sample.ConfigTopic }));

        store.Read().Devices.Clear();

        Assert.Single(store.Read().Devices);
    }

    [Fact]
    public void TheRecordSitsBesideTheBrokerSettingsRatherThanInsideThem()
    {
        // What was published is state the layer discovers. Writing it as a setting would make a
        // successful announcement look like a settings change to anything listening for one.
        Assert.NotEqual(MqttSettingsFile.DefaultFileName, DiscoveryLedgerFile.DefaultFileName);
    }

    [Fact]
    public void AnUnreadableFileFallsBackToNothingRecordedRatherThanThrowing()
    {
        File.WriteAllText(Path.Combine(_directory, DiscoveryLedgerFile.DefaultFileName), "{ not json");

        var store = new DiscoveryLedgerFile(
            new SettingsFileOptions(_directory, DiscoveryLedgerFile.DefaultFileName));

        Assert.Empty(store.Read().Devices);
    }

    [Fact]
    public void AFileThatCannotBeReadIsNotWrittenOverOnceItIsReleased()
    {
        // A ledger locked when the store opens reads as nothing recorded. A write then must not put
        // that nothing over the record that was there, or eviction across the restart is lost.
        string path = Path.Combine(_directory, DiscoveryLedgerFile.DefaultFileName);
        DiscoveryLedgerFile.In(_directory).Update(
            ledger => ledger.Devices.Add(new PublishedDevice { ConfigTopic = Sample.ConfigTopic }));
        string before = File.ReadAllText(path);

        DiscoveryLedgerFile store;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            store = DiscoveryLedgerFile.In(_directory);
        store.Update(ledger => ledger.Devices.Add(new PublishedDevice { ConfigTopic = "other/config" }));

        Assert.Equal(before, File.ReadAllText(path));
    }
}

/// <summary>The default store, and the one configuration in which eviction does not survive a
/// restart.</summary>
public class TransientLedgerStoreTests
{
    [Fact]
    public void ItRemembersWithinTheProcess()
    {
        var store = new TransientLedgerStore();
        store.Update(ledger => ledger.Devices.Add(new PublishedDevice { ConfigTopic = Sample.ConfigTopic }));

        Assert.NotNull(store.Read().Find(Sample.DeviceId));
    }

    [Fact]
    public void ReadHandsBackASnapshot()
    {
        var store = new TransientLedgerStore();
        store.Update(ledger => ledger.Devices.Add(new PublishedDevice { ConfigTopic = Sample.ConfigTopic }));

        store.Read().Devices.Clear();

        Assert.Single(store.Read().Devices);
    }

    [Fact]
    public void ACopyIsDeepEnoughToBeOne()
    {
        var ledger = new DiscoveryLedger
        {
            Devices =
            [
                new PublishedDevice
                {
                    ConfigTopic = Sample.ConfigTopic,
                    Entities = [new PublishedEntity { EntityId = "cpu_load" }],
                },
            ],
        };

        var copy = ledger.Copy();
        copy.Devices[0].Entities[0].EntityId = "changed";
        copy.Devices[0].Entities.Add(new PublishedEntity { EntityId = "extra" });

        Assert.Equal("cpu_load", ledger.Devices[0].Entities.Single().EntityId);
    }
}
