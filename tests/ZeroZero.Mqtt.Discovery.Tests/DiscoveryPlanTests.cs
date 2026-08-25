using System.Text.Json.Nodes;
using Xunit;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The reconciliation, on its own. The ledger and the entity lists go in; the messages and
/// the next ledger come out, with no client and no clock in the way.</summary>
/// <remarks>What most of these are about is that the four reasons an entity stops being published are
/// four different things on the wire: deleted, withheld, migrating and retired.</remarks>
public class DiscoveryPlanTests
{
    private static DiscoveryPass Announce(
        DiscoveryLedger ledger,
        IReadOnlyList<MqttEntity> published,
        IReadOnlyList<MqttEntity>? withheld = null,
        IReadOnlyList<RetiredEntity>? retired = null,
        IReadOnlyList<MigratingEntity>? migrating = null,
        IReadOnlyList<RetiredChannel>? retiredChannels = null,
        MqttDeviceIdentity? identity = null) =>
        DiscoveryPlan.Announce(
            ledger, Sample.TopicRoot, identity ?? Sample.Identity, Sample.Device, Sample.Origin,
            published, withheld ?? [], retired ?? [], migrating ?? [], retiredChannels ?? [],
            "online", "offline");

    private static DiscoveryLedger LedgerWith(params PublishedEntity[] entities) => new()
    {
        Devices =
        [
            new PublishedDevice
            {
                DeviceId = Sample.DeviceId,
                ConfigTopic = Sample.ConfigTopic,
                AvailabilityTopic = Sample.Availability,
                Entities = [.. entities],
            },
        ],
    };

    private static PublishedEntity Recorded(string id, string platform = "sensor") =>
        new() { EntityId = id, Platform = platform, StateTopic = Sample.State(id) };

    private static JsonObject Components(DiscoveryPass pass) =>
        (JsonObject)JsonNode.Parse(pass.Document)!["cmps"]!;

    private static JsonObject Component(DiscoveryPass pass, string entityId) =>
        (JsonObject)Components(pass)[entityId]!;

    [Fact]
    public void AFirstPassEvictsNothingAndRecordsWhatItAnnounced()
    {
        var pass = Announce(new DiscoveryLedger(), [Sample.Sensor(), Sample.Button()]);

        Assert.Empty(pass.Evictions);
        Assert.Empty(pass.Sweep);
        Assert.Equal(Sample.ConfigTopic, pass.ConfigTopic);

        var recorded = pass.Ledger.Find(Sample.DeviceId)!;
        Assert.Equal(["cpu_load", "restart"], recorded.Entities.Select(e => e.EntityId));
        Assert.Equal(Sample.Availability, recorded.AvailabilityTopic);
        Assert.Equal(Sample.ConfigTopic, recorded.ConfigTopic);
    }

    [Fact]
    public void AButtonIsRecordedWithNoStateTopicToEvict()
    {
        var pass = Announce(new DiscoveryLedger(), [Sample.Button()]);
        var recorded = pass.Ledger.Find(Sample.DeviceId)!.Entities.Single();

        Assert.Equal("", recorded.StateTopic);
    }

    [Fact]
    public void ANonRetainingEntityIsRecordedWithNothingToEvict()
    {
        // Nothing is held on its topic, so an empty publish there would be a message about nothing.
        var pass = Announce(new DiscoveryLedger(), [Sample.Sensor(retain: false)]);

        Assert.Equal("", pass.Ledger.Find(Sample.DeviceId)!.Entities.Single().StateTopic);
    }

    [Fact]
    public void AnEntityTheTableNoLongerContainsIsRemovedAndSwept()
    {
        var pass = Announce(LedgerWith(Recorded("cpu_load"), Recorded("gone", "switch")), [Sample.Sensor()]);

        var stub = Component(pass, "gone");
        Assert.Equal(["p"], stub.Select(pair => pair.Key));
        Assert.Equal("switch", (string?)stub["p"]);

        Assert.Equal([Sample.State("gone")], pass.Sweep.Select(m => m.Topic));
        Assert.All(pass.Sweep, m => Assert.Equal("", m.Payload));
        Assert.All(pass.Sweep, m => Assert.True(m.Retain));
    }

    [Fact]
    public void ARemovedEntityLeavesTheRecordOnceItIsGone()
    {
        var pass = Announce(LedgerWith(Recorded("gone")), [Sample.Sensor()]);

        Assert.Equal(["cpu_load"], pass.Ledger.Find(Sample.DeviceId)!.Entities.Select(e => e.EntityId));
    }

    [Fact]
    public void AnEntityRemovedWhileTheApplicationWasClosedIsStillEvicted()
    {
        // Nothing in this process ever saw it. Diffing against the previous in-memory set would never
        // reach it, and its retained topics would stay on the broker for ever.
        var ledger = LedgerWith(Recorded("vm_alpha"), Recorded("vm_beta"));

        var pass = Announce(ledger, [Sample.Sensor("vm_alpha")]);

        Assert.Contains(Sample.State("vm_beta"), pass.Sweep.Select(m => m.Topic));
        Assert.Equal(["p"], Component(pass, "vm_beta").Select(pair => pair.Key));
    }

    [Fact]
    public void AWithheldEntityKeepsItsWholeComponentAndIsAnnouncedUnavailable()
    {
        // A group switched off and an Include gone false are reversible, and a reversible action
        // must not be announced as a removal. The component stays whole — every key the receiver
        // files it by — and an availability topic of its own says it is not reporting.
        var pass = Announce(
            LedgerWith(Recorded("cpu_load"), Recorded("gpu_load")),
            [Sample.Sensor("cpu_load")],
            withheld: [Sample.Sensor("gpu_load")]);

        var component = Component(pass, "gpu_load");
        Assert.Equal("sensor", (string?)component["p"]);
        Assert.Equal($"{Sample.DeviceId}_gpu_load", (string?)component["unique_id"]);
        Assert.Equal(Sample.Withheld, (string?)component["availability_topic"]);
        Assert.True(component.Count > 1, "a withheld component was written as a removal stub");
    }

    [Fact]
    public void TheWithheldTopicSaysOfflineAndSaysItBeforeTheDocument()
    {
        // Every withheld component points at it, so it must already carry the offline payload by the
        // time the document naming it arrives.
        var pass = Announce(
            LedgerWith(Recorded("gpu_load")), [], withheld: [Sample.Sensor("gpu_load")]);

        var marker = pass.Evictions.Single(m => m.Topic == Sample.Withheld);
        Assert.Equal("offline", marker.Payload);
        Assert.True(marker.Retain);
    }

    [Fact]
    public void TheWithheldTopicIsWrittenOnceRatherThanOnEveryPass()
    {
        var first = Announce(LedgerWith(Recorded("gpu_load")), [], withheld: [Sample.Sensor("gpu_load")]);
        var second = Announce(first.Ledger, [], withheld: [Sample.Sensor("gpu_load")]);

        Assert.DoesNotContain(Sample.Withheld, second.Evictions.Select(m => m.Topic));
    }

    [Fact]
    public void AWithheldEntityGivesUpItsValueAndKeepsItsEntry()
    {
        // The value goes — nothing should be left standing and stale on the broker — but the record
        // keeps the entity, so it is still in the document and still has somewhere to come back to.
        var pass = Announce(
            LedgerWith(Recorded("gpu_load")), [], withheld: [Sample.Sensor("gpu_load")]);

        Assert.Contains(Sample.State("gpu_load"), pass.Sweep.Select(m => m.Topic));

        var recorded = pass.Ledger.Find(Sample.DeviceId)!.Entities.Single();
        Assert.Equal("gpu_load", recorded.EntityId);
        Assert.True(recorded.Withheld);
        Assert.Equal("", recorded.StateTopic);
    }

    [Fact]
    public void AWithheldEntityComesBackWholeWhenItIsPublishedAgain()
    {
        var off = Announce(LedgerWith(Recorded("gpu_load")), [], withheld: [Sample.Sensor("gpu_load")]);
        var on = Announce(off.Ledger, [Sample.Sensor("gpu_load")]);

        Assert.False(Component(on, "gpu_load").ContainsKey("availability_topic"));
        Assert.Equal(Sample.State("gpu_load"), on.Ledger.Find(Sample.DeviceId)!.Entities.Single().StateTopic);
        Assert.False(on.Ledger.Find(Sample.DeviceId)!.Entities.Single().Withheld);
    }

    [Fact]
    public void AnEntityWithheldBeforeItWasEverAnnouncedIsLeftOutRatherThanCreated()
    {
        // A group that has never been switched on has no entry to protect, and announcing one would
        // create in the receiver exactly what the user declined.
        var pass = Announce(new DiscoveryLedger(), [Sample.Sensor()], withheld: [Sample.Sensor("gpu_load")]);

        Assert.False(Components(pass).ContainsKey("gpu_load"));
    }

    [Fact]
    public void AnEntityThatKeepsItsIdAndLosesItsStateTopicIsStillSwept()
    {
        // Compared as topics, not as ids: the id is still in the record, so an id-only comparison
        // would leave the value it left behind retained on the broker for ever.
        var pass = Announce(LedgerWith(Recorded("cpu_load")), [Sample.Sensor(retain: false)]);

        Assert.Equal([Sample.State("cpu_load")], pass.Sweep.Select(m => m.Topic));
        Assert.Equal(["cpu_load"], pass.Ledger.Find(Sample.DeviceId)!.Entities.Select(e => e.EntityId));
    }

    [Fact]
    public void AnIdentityTheRecordNamesAndTheConfigurationDoesNotIsAbandonedWhole()
    {
        var ledger = new DiscoveryLedger
        {
            Devices =
            [
                new PublishedDevice
                {
                    DeviceId = "exampleapp_old",
                    ConfigTopic = DiscoveryTopics.Device(Sample.Prefix, "exampleapp_old"),
                    AvailabilityTopic = MqttTopics.Availability(Sample.TopicRoot, "exampleapp_old"),
                    Entities =
                    [
                        new PublishedEntity
                        {
                            EntityId = "cpu_load",
                            Platform = "sensor",
                            StateTopic = MqttTopics.Channel(Sample.TopicRoot, "exampleapp_old", "cpu_load"),
                        },
                    ],
                },
            ],
        };

        var pass = Announce(ledger, [Sample.Sensor()]);

        Assert.Equal(
            [
                DiscoveryTopics.Device(Sample.Prefix, "exampleapp_old"),
                MqttTopics.Availability(Sample.TopicRoot, "exampleapp_old"),
                MqttTopics.Channel(Sample.TopicRoot, "exampleapp_old", "cpu_load"),
            ],
            pass.Evictions.Select(m => m.Topic));
        Assert.All(pass.Evictions, m => Assert.Equal("", m.Payload));
        Assert.Null(pass.Ledger.Find("exampleapp_old"));
    }

    [Fact]
    public void ARecordWrittenBeforeTheDeviceIdWasStoredKeepsItsIdentity()
    {
        // Only the topic was recorded. Read as a different device it would abandon the live one:
        // empty its document, its availability and every value under it, and then rebuild it.
        var ledger = new DiscoveryLedger
        {
            Devices = [new PublishedDevice { ConfigTopic = Sample.ConfigTopic, Entities = [Recorded("cpu_load")] }],
        };

        var pass = Announce(ledger, [Sample.Sensor()]);

        Assert.Empty(pass.Evictions);
        Assert.Single(pass.Ledger.Devices);
        Assert.Equal(Sample.DeviceId, pass.Ledger.Devices[0].DeviceId);
    }

    [Fact]
    public void AMovedDiscoveryPrefixIsTheSameDeviceAtANewAddress()
    {
        // The prefix decides where the document is written and nothing else: every unique id, the
        // availability topic and every state topic are composed without it. Abandoning on it would
        // empty the live device's own availability and every current value under it.
        var pass = Announce(
            LedgerWith(Recorded("cpu_load")),
            [Sample.Sensor()],
            identity: new MqttDeviceIdentity(Sample.DeviceId, "ha", Sample.DeviceName));

        Assert.Equal(DiscoveryTopics.Device("ha", Sample.DeviceId), pass.ConfigTopic);
        Assert.DoesNotContain(Sample.Availability, pass.Evictions.Select(m => m.Topic));
        Assert.DoesNotContain(Sample.State("cpu_load"), pass.Evictions.Select(m => m.Topic));
        Assert.DoesNotContain(Sample.State("cpu_load"), pass.Sweep.Select(m => m.Topic));
        Assert.Single(pass.Ledger.Devices);
    }

    [Fact]
    public void AMovedDiscoveryPrefixClearsItsOldAddressAfterTheNewDocument()
    {
        // Not first: clearing before publishing would leave a window with no device anywhere. Not
        // never either — a retained config under the old prefix would be resurrected whole by a
        // receiver later pointed at it, with nothing left that knows to evict it.
        var pass = Announce(
            LedgerWith(Recorded("cpu_load")),
            [Sample.Sensor()],
            identity: new MqttDeviceIdentity(Sample.DeviceId, "ha", Sample.DeviceName));

        Assert.Equal([Sample.ConfigTopic], pass.Sweep.Select(m => m.Topic));
        Assert.All(pass.Sweep, m => Assert.Equal("", m.Payload));
        Assert.Equal(DiscoveryTopics.Device("ha", Sample.DeviceId), pass.Ledger.Find(Sample.DeviceId)!.ConfigTopic);
    }

    [Fact]
    public void ARetiredEntityIsEmptiedAtItsOwnPerComponentPath()
    {
        var pass = Announce(
            new DiscoveryLedger(), [Sample.Sensor()], retired: [new RetiredEntity("sensor", "old_name")]);

        Assert.Equal(
            [DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "old_name")],
            pass.Evictions.Select(m => m.Topic));
        Assert.All(pass.Evictions, m => Assert.Equal("", m.Payload));
    }

    [Fact]
    public void ARetirementHappensOnceAndIsWrittenDownAsATopic()
    {
        // Repeated on every connect it costs a publish each time and re-runs a removal against a
        // path a consumer may since have started using again. Recorded as the composed topic,
        // component segment and all, because that is the thing that was emptied.
        var retired = new[] { new RetiredEntity("sensor", "old_name") };
        string topic = DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "old_name");

        var first = Announce(new DiscoveryLedger(), [Sample.Sensor()], retired: retired);
        var second = Announce(first.Ledger, [Sample.Sensor()], retired: retired);

        Assert.Equal([topic], first.Evictions.Select(m => m.Topic));
        Assert.Empty(second.Evictions);
        Assert.Equal([topic], first.Ledger.Find(Sample.DeviceId)!.Retired);
    }

    [Fact]
    public void ARetirementUnderOneComponentLeavesAnotherComponentsLiveEntityAlone()
    {
        // The unique id carries no component, but the config topic does, and so does the receiver's
        // own scoping. Retiring binary_sensor/smart_charge must not touch switch/smart_charge.
        var pass = Announce(
            new DiscoveryLedger(),
            [Sample.Switch("smart_charge")],
            retired: [new RetiredEntity("binary_sensor", "smart_charge")]);

        Assert.Equal(
            [DiscoveryTopics.Component(Sample.Prefix, "binary_sensor", Sample.DeviceId, "smart_charge")],
            pass.Evictions.Select(m => m.Topic));
        Assert.DoesNotContain(
            DiscoveryTopics.Component(Sample.Prefix, "switch", Sample.DeviceId, "smart_charge"),
            pass.Evictions.Select(m => m.Topic));
    }

    [Fact]
    public void ARetiredChannelIsEmptiedInTheSweepRatherThanAmongTheEvictions()
    {
        // A value topic, not a config: nothing about it has to precede the document, so it goes with
        // the other value topics the sweep empties.
        var pass = Announce(
            new DiscoveryLedger(), [Sample.Sensor()], retiredChannels: [new RetiredChannel("legacy_state")]);

        Assert.Empty(pass.Evictions);
        Assert.Equal([Sample.State("legacy_state")], pass.Sweep.Select(m => m.Topic));
        Assert.Equal("", pass.Sweep[0].Payload);
        Assert.True(pass.Sweep[0].Retain);
    }

    [Fact]
    public void ARetiredChannelIsEmptiedOnceAndWrittenDownAsATopic()
    {
        // As with a retirement: repeating it on every connect spends a publish each time and re-runs
        // a removal against a key a consumer may since have started using again.
        var retiredChannels = new[] { new RetiredChannel("legacy_state") };

        var first = Announce(new DiscoveryLedger(), [Sample.Sensor()], retiredChannels: retiredChannels);
        var second = Announce(first.Ledger, [Sample.Sensor()], retiredChannels: retiredChannels);

        Assert.Equal([Sample.State("legacy_state")], first.Ledger.Find(Sample.DeviceId)!.RetiredChannels);
        Assert.Empty(second.Sweep);
        Assert.Empty(second.Evictions);
    }

    [Fact]
    public void ARetiredChannelIsKeptApartFromTheRetiredConfigTopics()
    {
        // Two different subtrees. One list read as the other would empty a path nothing published on.
        var pass = Announce(
            new DiscoveryLedger(), [Sample.Sensor()],
            retired: [new RetiredEntity("sensor", "old_name")],
            retiredChannels: [new RetiredChannel("legacy_state")]);

        var recorded = pass.Ledger.Find(Sample.DeviceId)!;
        Assert.Equal(
            [DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "old_name")],
            recorded.Retired);
        Assert.Equal([Sample.State("legacy_state")], recorded.RetiredChannels);
    }

    [Fact]
    public void WithdrawEmptiesARetiredChannelWhetherOrNotTheRecordHasIt()
    {
        // A removal is final, so it does not depend on the record being complete.
        var recorded = LedgerWith(Recorded("cpu_load"));
        recorded.Find(Sample.DeviceId)!.RetiredChannels.Add(Sample.State("legacy_state"));

        var (fromRecord, _) = DiscoveryPlan.Withdraw(
            recorded, Sample.TopicRoot, Sample.Identity, [Sample.Sensor()], [], [],
            [new RetiredChannel("legacy_state")]);
        var (fromNothing, _) = DiscoveryPlan.Withdraw(
            new DiscoveryLedger(), Sample.TopicRoot, Sample.Identity, [Sample.Sensor()], [], [],
            [new RetiredChannel("legacy_state")]);

        Assert.Contains(Sample.State("legacy_state"), fromRecord.Select(m => m.Topic));
        Assert.Contains(Sample.State("legacy_state"), fromNothing.Select(m => m.Topic));
        Assert.All(fromNothing, m => Assert.Equal("", m.Payload));
    }

    [Fact]
    public void AMigrationFlagsTheOldTopicBeforeTheDocumentAndEmptiesItAfter()
    {
        string topic = DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "cpu_load");

        var pass = Announce(
            new DiscoveryLedger(), [Sample.Sensor()],
            migrating: [new MigratingEntity("sensor", "cpu_load")]);

        Assert.Equal([topic], pass.Evictions.Select(m => m.Topic));
        Assert.Equal(DiscoveryTopics.MigratePayload, pass.Evictions[0].Payload);
        Assert.True(pass.Evictions[0].Retain);

        Assert.Equal([topic], pass.Sweep.Select(m => m.Topic));
        Assert.Equal("", pass.Sweep[0].Payload);
    }

    [Fact]
    public void AMigrationIsNotReplayedAsARetirementOnTheNextConnect()
    {
        // The two write one topic with opposite intent. A consumer restarting — which the first one
        // through this does at every reboot, update and watchdog restart — must not re-run the
        // handover as a removal.
        var migrating = new[] { new MigratingEntity("sensor", "cpu_load") };
        string topic = DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "cpu_load");

        var first = Announce(new DiscoveryLedger(), [Sample.Sensor()], migrating: migrating);
        var second = Announce(first.Ledger, [Sample.Sensor()], migrating: migrating);

        Assert.Empty(second.Evictions);
        Assert.Empty(second.Sweep);
        Assert.Equal([topic], first.Ledger.Find(Sample.DeviceId)!.Migrated);
        Assert.Empty(first.Ledger.Find(Sample.DeviceId)!.Retired);
    }

    [Fact]
    public void ARetirementNeverTouchesATopicAMigrationHasAlreadyHandedOver()
    {
        var migrated = Announce(
            new DiscoveryLedger(), [Sample.Sensor()],
            migrating: [new MigratingEntity("sensor", "cpu_load")]);

        var later = Announce(
            migrated.Ledger, [Sample.Sensor()],
            retired: [new RetiredEntity("sensor", "cpu_load")]);

        Assert.Empty(later.Evictions);
    }

    [Fact]
    public void WithdrawEmptiesEverythingTheIdentityOwns()
    {
        var (messages, ledger) = DiscoveryPlan.Withdraw(
            LedgerWith(Recorded("cpu_load"), Recorded("restart", "button")),
            Sample.TopicRoot, Sample.Identity, [Sample.Sensor(), Sample.Button()],
            [new RetiredEntity("sensor", "old_name")], [], []);

        Assert.Equal(
            [
                Sample.ConfigTopic,
                Sample.Availability,
                Sample.Withheld,
                Sample.State("cpu_load"),
                Sample.State("restart"),
                Sample.Command("restart"),
                DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "old_name"),
            ],
            messages.Select(m => m.Topic));
        Assert.All(messages, m => Assert.Equal("", m.Payload));
        Assert.Empty(ledger.Devices);
    }

    [Fact]
    public void WithdrawEmptiesTheCommandTopicsToo()
    {
        // This layer publishes nothing there, but something else can leave a retained command
        // standing, and it is redelivered the moment anything subscribes again.
        var (messages, _) = DiscoveryPlan.Withdraw(
            new DiscoveryLedger(), Sample.TopicRoot, Sample.Identity, [Sample.Switch()], [], [], []);

        Assert.Contains(Sample.Command("quiet_mode"), messages.Select(m => m.Topic));
    }

    [Fact]
    public void WithdrawEmptiesTheAvailabilityAndTheValuesEvenWithNoRecordToGoOn()
    {
        // A first run, or a record that was lost. Clearing only the document would leave the
        // availability topic and every value under it standing on the broker.
        var (messages, _) = DiscoveryPlan.Withdraw(
            new DiscoveryLedger(), Sample.TopicRoot, Sample.Identity, [Sample.Sensor()], [], [], []);

        Assert.Equal(
            [Sample.ConfigTopic, Sample.Availability, Sample.Withheld, Sample.State("cpu_load")],
            messages.Select(m => m.Topic));
    }

    [Fact]
    public void ThePassLeavesTheLedgerItWasGivenAlone()
    {
        // It is written down only once the messages have landed, so a half-landed pass must not have
        // mutated what the next connect reconciles against.
        var ledger = LedgerWith(Recorded("gone"));

        _ = Announce(ledger, [Sample.Sensor()], retired: [new RetiredEntity("sensor", "old_name")]);

        Assert.Equal(["gone"], ledger.Find(Sample.DeviceId)!.Entities.Select(e => e.EntityId));
        Assert.Empty(ledger.Find(Sample.DeviceId)!.Retired);
    }

    [Fact]
    public void TheAnnouncedDocumentDescribesWhatWasPublished()
    {
        var pass = Announce(new DiscoveryLedger(), [Sample.Sensor(), Sample.Switch()]);

        Assert.Equal(["cpu_load", "quiet_mode"], Components(pass).Select(pair => pair.Key));
    }
}
