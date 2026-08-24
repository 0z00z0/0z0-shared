using System.Text.Json.Nodes;
using Xunit;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The publisher against a recording double: what reaches the wire on connect, when the set
/// changes, when a group moves and when publishing stops — and what a second run over the same record
/// evicts.</summary>
public class DiscoveryPublisherTests
{
    /// <summary>A publisher, its broker, its record, and the two callbacks a connection would have
    /// supplied. Everything a pass touches, so a test asserts on all of it at once.</summary>
    private sealed class Harness : IDisposable
    {
        public RecordingPublisher Broker { get; } = new();

        public RecordingLedgerStore Ledger { get; }

        public PublishGroupSet Groups { get; }

        public bool Connected { get; set; } = true;

        public List<IReadOnlyList<MqttChannel>> ChannelSets { get; } = [];

        public List<IReadOnlyList<MqttCommandTarget>> TargetSets { get; } = [];

        public DiscoveryPublisher Publisher { get; }

        public Harness(
            MqttEntitySet entities,
            RecordingLedgerStore? ledger = null,
            IReadOnlyList<RetiredEntity>? retired = null,
            params PublishGroup[] groups)
        {
            Ledger = ledger ?? new RecordingLedgerStore();
            Groups = new PublishGroupSet(new MemorySettingsStore(), groups);
            Publisher = new DiscoveryPublisher(new DiscoveryPublisherSetup
            {
                IsConnected = () => Connected,
                TopicRoot = Sample.TopicRoot,
                Device = Sample.Device,
                Origin = Sample.Origin,
                Entities = entities,
                Groups = Groups,
                Retired = retired ?? [],
                Ledger = Ledger,
                SetChannelsAsync = (channels, _) => { ChannelSets.Add(channels); return Task.CompletedTask; },
                SetCommandTargets = TargetSets.Add,
                BirthRepublishDelay = TimeSpan.Zero,
            });
        }

        public IMqttConnectionListener Listener => Publisher;

        public Task ConnectAsync(MqttDeviceIdentity? identity = null) =>
            Listener.OnConnectedAsync(Broker, identity ?? Sample.Identity, CancellationToken.None);

        public Task StopAsync(MqttDeviceIdentity? identity = null) =>
            Listener.OnStoppingAsync(Broker, identity ?? Sample.Identity, CancellationToken.None);

        public JsonObject Document()
        {
            string? json = Broker.Last(Sample.ConfigTopic);
            Assert.NotNull(json);
            return (JsonObject)JsonNode.Parse(json)!;
        }

        public JsonObject Components() => (JsonObject)Document()["cmps"]!;

        public void Dispose() => Publisher.Dispose();
    }

    [Fact]
    public async Task ConnectAnnouncesTheDeviceAsOneRetainedDocument()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor(), Sample.Switch(), Sample.Button()]));

        await harness.ConnectAsync();

        var document = harness.Broker.Messages.Single(m => m.Topic == Sample.ConfigTopic);
        Assert.True(document.Retain);
        Assert.Equal(MqttQos.AtLeastOnce, document.Qos);
        Assert.Equal(["cpu_load", "quiet_mode", "restart"], harness.Components().Select(p => p.Key));
    }

    [Fact]
    public async Task ConnectHandsTheConnectionItsChannelsAndItsCommandTargets()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor(), Sample.Switch(), Sample.Button()]));

        await harness.ConnectAsync();

        Assert.Equal(["cpu_load", "quiet_mode"], harness.ChannelSets[^1].Select(c => c.Key));
        Assert.Equal(["quiet_mode", "restart"], harness.TargetSets[^1].Select(t => t.EntityId));
    }

    [Fact]
    public async Task ConnectEmptiesTheConfigsOfEntitiesTheConsumerRenamedLongAgo()
    {
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor()]), retired: [new RetiredEntity("switch", "old_name")]);

        await harness.ConnectAsync();

        Assert.True(harness.Broker.Emptied(
            DiscoveryTopics.Component(Sample.Prefix, "switch", Sample.DeviceId, "old_name")));
    }

    [Fact]
    public async Task ConnectRecordsWhatItAnnounced()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor(), Sample.Button()]));

        await harness.ConnectAsync();

        var recorded = harness.Ledger.Read().Find(Sample.ConfigTopic)!;
        Assert.Equal(["cpu_load", "restart"], recorded.Entities.Select(e => e.EntityId));
        Assert.Equal(Sample.State("cpu_load"), recorded.Entities[0].StateTopic);
        Assert.Equal("", recorded.Entities[1].StateTopic);
    }

    [Fact]
    public async Task SetEntitiesRemovesTheComponentAndEmptiesTheStateTopic()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor("cpu_load"), Sample.Sensor("gpu_load")]));
        await harness.ConnectAsync();
        harness.Broker.Forget();

        await harness.Publisher.SetEntitiesAsync(new MqttEntitySet([Sample.Sensor("cpu_load")]));

        Assert.Equal(["p"], ((JsonObject)harness.Components()["gpu_load"]!).Select(p => p.Key));
        Assert.True(harness.Broker.Emptied(Sample.State("gpu_load")));
        Assert.DoesNotContain(Sample.State("cpu_load"), harness.Broker.Topics);
    }

    [Fact]
    public async Task EvictionSurvivesAProcessRestart()
    {
        // The whole point of the record. One run publishes two entities; the application closes; the
        // next run publishes one. Nothing in the second process ever saw the entity that went, so a
        // diff against the previous in-memory set would leave its retained topics on the broker for
        // ever — which is exactly the case a per-machine entity set reaches on its first removal.
        var ledger = new RecordingLedgerStore();

        using (var first = new Harness(
            new MqttEntitySet([Sample.Sensor("vm_alpha"), Sample.Sensor("vm_beta")]), ledger))
        {
            await first.ConnectAsync();
        }

        using var second = new Harness(new MqttEntitySet([Sample.Sensor("vm_alpha")]), ledger);
        await second.ConnectAsync();

        Assert.Equal(["p"], ((JsonObject)second.Components()["vm_beta"]!).Select(p => p.Key));
        Assert.True(second.Broker.Emptied(Sample.State("vm_beta")));
        Assert.Equal(["vm_alpha"], ledger.Read().Find(Sample.ConfigTopic)!.Entities.Select(e => e.EntityId));
    }

    [Fact]
    public async Task ADeviceIdChangedWhileTheApplicationWasClosedIsStillAbandoned()
    {
        var ledger = new RecordingLedgerStore();
        var old = new MqttDeviceIdentity("exampleapp_old", Sample.Prefix, "Old");

        using (var first = new Harness(new MqttEntitySet([Sample.Sensor()]), ledger))
        {
            await first.ConnectAsync(old);
        }

        using var second = new Harness(new MqttEntitySet([Sample.Sensor()]), ledger);
        await second.ConnectAsync();

        Assert.True(second.Broker.Emptied(DiscoveryTopics.Device(Sample.Prefix, "exampleapp_old")));
        Assert.True(second.Broker.Emptied(MqttTopics.Availability(Sample.TopicRoot, "exampleapp_old")));
        Assert.True(second.Broker.Emptied(MqttTopics.Channel(Sample.TopicRoot, "exampleapp_old", "cpu_load")));
        Assert.Single(ledger.Read().Devices);
    }

    [Fact]
    public async Task AChangedOptionListForcesARebuild()
    {
        // The set's members are unchanged: only the strings an Options delegate returns differ.
        // Comparing entity identity, or comparing the entity objects, misses it entirely.
        List<string> options = ["Office", "Home"];
        var set = new MqttEntitySet([Sample.Select(options: () => options)]);
        using var harness = new Harness(set);

        await harness.ConnectAsync();
        harness.Broker.Forget();

        options[1] = "Workshop";
        await harness.Publisher.SetEntitiesAsync(set);

        Assert.Equal(1, harness.Broker.CountOn(Sample.ConfigTopic));
        Assert.Equal(
            ["Office", "Workshop", MqttSelect.DefaultNoOption],
            ((JsonObject)harness.Components()["profile"]!)["options"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public async Task ARepublishThatChangesNothingSendsNothing()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Select()]));
        await harness.ConnectAsync();
        harness.Broker.Forget();

        await harness.Publisher.RepublishAsync();

        Assert.Equal(0, harness.Broker.RoundTrips);
    }

    [Fact]
    public async Task AGroupToggleRebuildsWithoutOnePublishPerEntity()
    {
        var entities = Enumerable.Range(0, 12).Select(i => Sample.Sensor($"metric_{i}", group: "metrics"));
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor("cpu_load"), .. entities]),
            groups: new PublishGroup("metrics", "Metrics"));

        await harness.ConnectAsync();
        harness.Broker.Forget();

        harness.Groups.Set("metrics", false);
        await WaitForAsync(() => harness.Broker.CountOn(Sample.ConfigTopic) == 1);

        // One document, and one batch for the twelve state topics — not twelve round trips.
        Assert.Equal(2, harness.Broker.RoundTrips);
        Assert.Equal(
            [.. Enumerable.Range(0, 12).Select(i => Sample.State($"metric_{i}"))],
            harness.Broker.Calls.Single(c => c.Count > 1).Select(m => m.Topic));
    }

    [Fact]
    public async Task AGroupSwitchedOffLeavesItsEntitiesRemovedNotUnavailable()
    {
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor("cpu_load"), Sample.Sensor("gpu_load", group: "metrics")]),
            groups: new PublishGroup("metrics", "Metrics"));
        await harness.ConnectAsync();

        harness.Groups.Set("metrics", false);
        await WaitForAsync(() => harness.Broker.Emptied(Sample.State("gpu_load")));

        Assert.Equal(["p"], ((JsonObject)harness.Components()["gpu_load"]!).Select(p => p.Key));
    }

    [Fact]
    public async Task NothingIsAnnouncedWhileDisconnected()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor()])) { Connected = false };

        await harness.Publisher.SetEntitiesAsync(new MqttEntitySet([Sample.Sensor(), Sample.Button()]));

        Assert.Empty(harness.Broker.Messages);
        // The projection still lands, so the next connect announces the right thing.
        Assert.Equal(["cpu_load"], harness.ChannelSets[^1].Select(c => c.Key));
        Assert.Equal(["restart"], harness.TargetSets[^1].Select(t => t.EntityId));
    }

    [Fact]
    public async Task NothingIsAnnouncedBeforeTheFirstConnect()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor()]));

        await harness.Publisher.RepublishAsync();

        Assert.Empty(harness.Broker.Messages);
    }

    [Fact]
    public async Task APassThatDidNotLandIsNotWrittenDown()
    {
        // A value recorded as published and never sent is the one state nothing recovers from: the
        // next connect would reconcile against a record of something the broker never received.
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor()]));
        harness.Broker.Refuses = topic => topic == Sample.ConfigTopic;

        await harness.ConnectAsync();

        Assert.Equal(0, harness.Ledger.Writes);
        Assert.Empty(harness.Ledger.Read().Devices);
    }

    [Fact]
    public async Task APassThatDidNotLandIsSentAgainNextTime()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor()]));
        harness.Broker.Refuses = topic => topic == Sample.ConfigTopic;
        await harness.ConnectAsync();

        harness.Broker.Refuses = _ => false;
        await harness.Publisher.RepublishAsync();

        Assert.Equal(2, harness.Broker.CountOn(Sample.ConfigTopic));
        Assert.Equal(1, harness.Ledger.Writes);
    }

    [Fact]
    public async Task StoppingRemovesTheWholeDevice()
    {
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor(), Sample.Button()]),
            retired: [new RetiredEntity("sensor", "old_name")]);
        await harness.ConnectAsync();
        harness.Broker.Forget();

        await harness.StopAsync();

        // A zero-length retained payload at the config topic is what removes the device outright.
        Assert.True(harness.Broker.Emptied(Sample.ConfigTopic));
        Assert.True(harness.Broker.Emptied(Sample.Availability));
        Assert.True(harness.Broker.Emptied(Sample.State("cpu_load")));
        Assert.Empty(harness.Ledger.Read().Devices);
    }

    [Fact]
    public async Task ASupersededIdentityShedsEverythingItOwned()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor()]));
        await harness.ConnectAsync();
        harness.Broker.Forget();

        await harness.Listener.OnIdentityRetiredAsync(harness.Broker, Sample.Identity, CancellationToken.None);

        Assert.True(harness.Broker.Emptied(Sample.ConfigTopic));
        Assert.True(harness.Broker.Emptied(Sample.State("cpu_load")));
    }

    [Fact]
    public async Task AnIdentityWithNoDeviceIdIsNotAnnouncedTo()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor()]));

        await harness.ConnectAsync(new MqttDeviceIdentity("", Sample.Prefix, ""));

        Assert.Empty(harness.Broker.Messages);
    }

    [Fact]
    public async Task ABirthMessageBringsTheDeviceBack()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor()]));
        await harness.ConnectAsync();
        harness.Broker.Forget();

        var subscription = harness.Publisher.BirthMessage(Sample.Prefix);
        Assert.Equal("homeassistant/status", subscription.TopicFilter);

        await subscription.Handler(
            new MqttInboundMessage(subscription.TopicFilter, "online", false), CancellationToken.None);

        Assert.Equal(1, harness.Broker.CountOn(Sample.ConfigTopic));
    }

    [Fact]
    public async Task AReceiversWillIsNotABirthMessage()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor()]));
        await harness.ConnectAsync();
        harness.Broker.Forget();

        var subscription = harness.Publisher.BirthMessage(Sample.Prefix);
        await subscription.Handler(
            new MqttInboundMessage(subscription.TopicFilter, "offline", true), CancellationToken.None);

        Assert.Empty(harness.Broker.Messages);
    }

    [Fact]
    public async Task ASelectsChannelNeverCarriesAnEmptyPayload()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Select(read: () => null)]));

        await harness.ConnectAsync();

        var channel = harness.ChannelSets[^1].Single();
        Assert.Equal(MqttSelect.DefaultNoOption, channel.Payload());
    }

    [Fact]
    public async Task ReconnectingReAnnouncesWhateverTheBrokerMayHaveLost()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor()]));
        await harness.ConnectAsync();
        harness.Broker.Forget();

        await harness.ConnectAsync();

        Assert.Equal(1, harness.Broker.CountOn(Sample.ConfigTopic));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "The condition did not hold in time.");
    }
}
