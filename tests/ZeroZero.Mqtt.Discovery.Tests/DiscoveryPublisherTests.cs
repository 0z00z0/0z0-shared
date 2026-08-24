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
            IReadOnlyList<MigratingEntity>? migrating = null,
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
                Migrating = migrating ?? [],
                Ledger = Ledger,
                SetChannelsAsync = (channels, _) => { ChannelSets.Add(channels); return Task.CompletedTask; },
                SetCommandTargets = TargetSets.Add,
                BirthRepublishDelay = TimeSpan.Zero,
            });
        }

        public IMqttConnectionListener Listener => Publisher;

        public Task ConnectAsync(MqttDeviceIdentity? identity = null) =>
            Listener.OnConnectedAsync(Broker, identity ?? Sample.Identity, CancellationToken.None);

        public Task RemoveAsync(MqttDeviceIdentity? identity = null) =>
            Listener.OnRemovingAsync(Broker, identity ?? Sample.Identity, CancellationToken.None);

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
    public async Task ARetirementIsMadeOnTheFirstConnectAndNotOnEveryOneAfterIt()
    {
        // Repeated on every connect it costs a publish after every network blip, resume and receiver
        // restart, and re-runs a removal against a path a consumer may since have started using
        // again. The eviction lands before the document, so nothing local ever shows it happening.
        string topic = DiscoveryTopics.Component(Sample.Prefix, "switch", Sample.DeviceId, "old_name");
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor()]), retired: [new RetiredEntity("switch", "old_name")]);

        await harness.ConnectAsync();
        Assert.True(harness.Broker.Emptied(topic));

        harness.Broker.Forget();
        await harness.ConnectAsync();

        Assert.Equal(0, harness.Broker.CountOn(topic));
    }

    [Fact]
    public async Task ARetirementSurvivesARestartAsAThingAlreadyDone()
    {
        string topic = DiscoveryTopics.Component(Sample.Prefix, "switch", Sample.DeviceId, "old_name");
        var ledger = new RecordingLedgerStore();
        var retired = new[] { new RetiredEntity("switch", "old_name") };

        using (var first = new Harness(new MqttEntitySet([Sample.Sensor()]), ledger, retired))
            await first.ConnectAsync();

        using var second = new Harness(new MqttEntitySet([Sample.Sensor()]), ledger, retired);
        await second.ConnectAsync();

        Assert.Equal(0, second.Broker.CountOn(topic));
        Assert.Equal([topic], ledger.Read().Find(Sample.DeviceId)!.Retired);
    }

    [Fact]
    public void ARetiredEntryMayNotNameALiveEntityOfTheSameComponent()
    {
        // One config topic with two owners: the retirement would empty the very path the live entity
        // is published at. Refused where it is declared rather than discovered on the wire.
        var error = Assert.Throws<ArgumentException>(() => new Harness(
            new MqttEntitySet([Sample.Switch("smart_charge")]),
            retired: [new RetiredEntity("switch", "smart_charge")]));

        Assert.Contains("smart_charge", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetiredEntryMayNameALiveEntityOfADifferentComponent()
    {
        // Two components, two config topics, two entries in the receiver's own registry. Refusing
        // this would refuse a shipping consumer's real renaming history.
        using var harness = new Harness(
            new MqttEntitySet([Sample.Switch("smart_charge")]),
            retired: [new RetiredEntity("binary_sensor", "smart_charge")]);

        await harness.ConnectAsync();

        Assert.True(harness.Broker.Emptied(
            DiscoveryTopics.Component(Sample.Prefix, "binary_sensor", Sample.DeviceId, "smart_charge")));
        Assert.Equal(0, harness.Broker.CountOn(
            DiscoveryTopics.Component(Sample.Prefix, "switch", Sample.DeviceId, "smart_charge")));
    }

    [Fact]
    public void AnEntityMayNotBeBothRetiredAndMigrating()
    {
        // Neither is a live entity, so this is refused on the contradiction alone: one declaration
        // empties the topic the other hands over, and the two intents cannot both be honoured.
        var error = Assert.Throws<ArgumentException>(() => new Harness(
            new MqttEntitySet([Sample.Sensor()]),
            retired: [new RetiredEntity("sensor", "old_name")],
            migrating: [new MigratingEntity("sensor", "old_name")]));

        Assert.Contains("old_name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSamePairUnderDifferentComponentsIsNotAContradiction() =>
        Assert.NotNull(new Harness(
            new MqttEntitySet([Sample.Sensor()]),
            retired: [new RetiredEntity("binary_sensor", "old_name")],
            migrating: [new MigratingEntity("switch", "old_name")]));

    [Fact]
    public async Task AMigrationHandsTheOldTopicOverBeforeTheDocumentAndClearsItAfter()
    {
        string topic = DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "cpu_load");
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor()]),
            migrating: [new MigratingEntity("sensor", "cpu_load")]);

        await harness.ConnectAsync();

        var order = harness.Broker.Messages.ToList();
        int flag = order.FindIndex(m => m.Topic == topic && m.Payload == DiscoveryTopics.MigratePayload);
        int document = order.FindIndex(m => m.Topic == Sample.ConfigTopic);
        int cleanup = order.FindLastIndex(m => m.Topic == topic && m.Payload.Length == 0);

        Assert.True(flag >= 0, "the migration flag never reached the broker");
        Assert.True(flag < document, "the flag must arrive before the document takes the entity over");
        Assert.True(document < cleanup, "the old topic must not be emptied before the document exists");
    }

    [Fact]
    public async Task AMigrationIsNotReplayedAsARetirementAfterARestart()
    {
        // The two write one topic with opposite intent, and the consumer this lands in restarts at
        // every reboot, every update and every watchdog restart — so a replay would be the rule.
        string topic = DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "cpu_load");
        var ledger = new RecordingLedgerStore();
        var migrating = new[] { new MigratingEntity("sensor", "cpu_load") };

        using (var first = new Harness(new MqttEntitySet([Sample.Sensor()]), ledger, migrating: migrating))
            await first.ConnectAsync();

        using var second = new Harness(new MqttEntitySet([Sample.Sensor()]), ledger, migrating: migrating);
        await second.ConnectAsync();

        Assert.Equal(0, second.Broker.CountOn(topic));

        var record = ledger.Read().Find(Sample.DeviceId)!;
        Assert.Equal([topic], record.Migrated);
        Assert.Empty(record.Retired);
    }

    [Fact]
    public async Task ConnectRecordsWhatItAnnounced()
    {
        using var harness = new Harness(new MqttEntitySet([Sample.Sensor(), Sample.Button()]));

        await harness.ConnectAsync();

        var recorded = harness.Ledger.Read().Find(Sample.DeviceId)!;
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
        Assert.Equal(["vm_alpha"], ledger.Read().Find(Sample.DeviceId)!.Entities.Select(e => e.EntityId));
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
        IReadOnlyList<string> options = ["Office", "Home"];
        var set = new MqttEntitySet([Sample.Select(options: () => options)]);
        using var harness = new Harness(set);

        await harness.ConnectAsync();
        harness.Broker.Forget();

        // A different list, not the same one mutated: the delegate has to be asked again.
        options = ["Office", "Workshop"];
        await harness.Publisher.SetEntitiesAsync(set);

        Assert.Equal(1, harness.Broker.CountOn(Sample.ConfigTopic));
        Assert.Equal(
            ["Office", "Workshop"],
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

        // One batch saying the withheld topic is offline, one document, and one batch for the twelve
        // state topics — not twelve round trips.
        Assert.Equal(3, harness.Broker.RoundTrips);
        Assert.Equal(
            [.. Enumerable.Range(0, 12).Select(i => Sample.State($"metric_{i}"))],
            harness.Broker.Calls.Single(c => c.Count > 1).Select(m => m.Topic));
    }

    [Fact]
    public async Task AGroupSwitchedOffLeavesItsEntitiesUnavailableNotRemoved()
    {
        // A settings checkbox that commits at once. Announcing a removal takes the entity off the
        // device page until it is re-ticked, and gives up the user's chosen entity id for good if
        // anything claims it in the gap.
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor("cpu_load"), Sample.Sensor("gpu_load", group: "metrics")]),
            groups: new PublishGroup("metrics", "Metrics"));
        await harness.ConnectAsync();

        harness.Groups.Set("metrics", false);
        await WaitForAsync(() => harness.Broker.Emptied(Sample.State("gpu_load")));

        var component = (JsonObject)harness.Components()["gpu_load"]!;
        Assert.NotEqual(["p"], component.Select(p => p.Key));
        Assert.Equal("CPU load", (string?)component["name"]);
        Assert.Equal(Sample.Withheld, (string?)component["availability_topic"]);
        Assert.Equal("offline", harness.Broker.Last(Sample.Withheld));
    }

    [Fact]
    public async Task AGroupSwitchedOffAndBackOnLeavesTheRecordWhereItStarted()
    {
        // A reversible action end to end: the entity keeps its entry throughout, and comes back
        // publishing on the same topic under the same unique id.
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor("gpu_load", group: "metrics")]),
            groups: new PublishGroup("metrics", "Metrics"));
        await harness.ConnectAsync();

        harness.Groups.Set("metrics", false);
        await WaitForAsync(() => harness.Ledger.Read().Find(Sample.DeviceId)!.Entities[0].Withheld);

        harness.Groups.Set("metrics", true);
        await WaitForAsync(() => !harness.Ledger.Read().Find(Sample.DeviceId)!.Entities[0].Withheld);

        var component = (JsonObject)harness.Components()["gpu_load"]!;
        Assert.False(component.ContainsKey("availability_topic"));
        Assert.Equal($"{Sample.DeviceId}_gpu_load", (string?)component["unique_id"]);
        Assert.Equal(
            Sample.State("gpu_load"),
            harness.Ledger.Read().Find(Sample.DeviceId)!.Entities[0].StateTopic);
    }

    [Fact]
    public async Task ACapabilityThatCannotBeReadKeepsWhateverWasAnnounced()
    {
        // The read fails after the entity has been announced. Reading that as "absent" would withhold
        // it — or, on a set that also lost the entity, remove it — on the strength of a controller
        // being busy for a moment, and a reconnect is exactly when that is most likely.
        bool reachable = true;
        Func<bool> gate = () => reachable ? true : throw new TimeoutException("the controller is busy");

        using var harness = new Harness(new MqttEntitySet([Sample.Sensor(include: gate)]));
        await harness.ConnectAsync();
        harness.Broker.Forget();

        reachable = false;
        await harness.Publisher.RepublishAsync();

        // Nothing moved at all: the document is unchanged, so it is not even re-sent.
        Assert.Equal(0, harness.Broker.RoundTrips);
        Assert.False(harness.Ledger.Read().Find(Sample.DeviceId)!.Entities[0].Withheld);
        Assert.Equal(["cpu_load"], harness.ChannelSets[^1].Select(c => c.Key));
    }

    [Fact]
    public async Task ACapabilityThatCannotBeReadKeepsAWithheldEntityWithheld()
    {
        int calls = 0;
        // False on the first pass, then unreadable: the record says withheld, so it stays withheld.
        Func<bool> gate = () => ++calls <= 1 ? false : throw new TimeoutException("the controller is busy");

        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor(), Sample.Sensor("gpu_load", include: gate)]));
        await harness.ConnectAsync();

        // Nothing was ever announced for it, so the first pass leaves it out altogether.
        Assert.False(harness.Components().ContainsKey("gpu_load"));
        Assert.Equal(["cpu_load"], harness.ChannelSets[^1].Select(c => c.Key));
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
    public async Task ADocumentThatDidNotLandHoldsBackTheSweepBehindIt()
    {
        // Everything in the sweep removes something the new document is meant to have taken over
        // first. Sent after a document that never arrived, it removes without replacing.
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor("cpu_load"), Sample.Sensor("gpu_load")]));
        await harness.ConnectAsync();
        harness.Broker.Forget();
        harness.Broker.Refuses = topic => topic == Sample.ConfigTopic;
        int writes = harness.Ledger.Writes;

        await harness.Publisher.SetEntitiesAsync(new MqttEntitySet([Sample.Sensor("cpu_load")]));

        Assert.Equal(0, harness.Broker.CountOn(Sample.State("gpu_load")));
        Assert.Equal(writes, harness.Ledger.Writes);
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
    public async Task RemovingTheDeviceEmptiesEverythingItOwns()
    {
        using var harness = new Harness(
            new MqttEntitySet([Sample.Sensor(), Sample.Button()]),
            retired: [new RetiredEntity("sensor", "old_name")]);
        await harness.ConnectAsync();
        harness.Broker.Forget();

        await harness.RemoveAsync();

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
        Assert.Equal(MqttPayload.None, channel.Payload());
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
