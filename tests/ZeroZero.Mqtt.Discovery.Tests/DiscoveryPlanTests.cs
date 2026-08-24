using System.Text.Json.Nodes;
using Xunit;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The reconciliation, on its own. The ledger and the entity list go in; the messages and the
/// next ledger come out, with no client and no clock in the way.</summary>
public class DiscoveryPlanTests
{
    private static DiscoveryPass Announce(
        DiscoveryLedger ledger,
        IReadOnlyList<MqttEntity> published,
        IReadOnlyList<RetiredEntity>? retired = null,
        bool includeRetired = true,
        MqttDeviceIdentity? identity = null) =>
        DiscoveryPlan.Announce(
            ledger, Sample.TopicRoot, identity ?? Sample.Identity, Sample.Device, Sample.Origin,
            published, retired ?? [], "online", "offline", includeRetired);

    private static DiscoveryLedger LedgerWith(params PublishedEntity[] entities) => new()
    {
        Devices =
        [
            new PublishedDevice
            {
                ConfigTopic = Sample.ConfigTopic,
                AvailabilityTopic = Sample.Availability,
                Entities = [.. entities],
            },
        ],
    };

    private static PublishedEntity Recorded(string id, string platform = "sensor") =>
        new() { EntityId = id, Platform = platform, StateTopic = Sample.State(id) };

    [Fact]
    public void AFirstPassEvictsNothingAndRecordsWhatItAnnounced()
    {
        var pass = Announce(new DiscoveryLedger(), [Sample.Sensor(), Sample.Button()]);

        Assert.Empty(pass.Evictions);
        Assert.Empty(pass.Sweep);
        Assert.Equal(Sample.ConfigTopic, pass.ConfigTopic);

        var recorded = pass.Ledger.Find(Sample.ConfigTopic)!;
        Assert.Equal(["cpu_load", "restart"], recorded.Entities.Select(e => e.EntityId));
        Assert.Equal(Sample.Availability, recorded.AvailabilityTopic);
    }

    [Fact]
    public void AButtonIsRecordedWithNoStateTopicToEvict()
    {
        var pass = Announce(new DiscoveryLedger(), [Sample.Button()]);
        var recorded = pass.Ledger.Find(Sample.ConfigTopic)!.Entities.Single();

        Assert.Equal("", recorded.StateTopic);
    }

    [Fact]
    public void AnEntityTheRecordNamesAndTheConfigurationDoesNotIsRemovedAndSwept()
    {
        var pass = Announce(LedgerWith(Recorded("cpu_load"), Recorded("gone", "switch")), [Sample.Sensor()]);

        var stub = (JsonObject)JsonNode.Parse(pass.Document)!["cmps"]!["gone"]!;
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

        Assert.Equal(["cpu_load"], pass.Ledger.Find(Sample.ConfigTopic)!.Entities.Select(e => e.EntityId));
    }

    [Fact]
    public void AnEntityRemovedWhileTheApplicationWasClosedIsStillEvicted()
    {
        // Nothing in this process ever saw it. Diffing against the previous in-memory set would never
        // reach it, and its retained topics would stay on the broker for ever.
        var ledger = LedgerWith(Recorded("vm_alpha"), Recorded("vm_beta"));

        var pass = Announce(ledger, [Sample.Sensor("vm_alpha")]);

        Assert.Contains(Sample.State("vm_beta"), pass.Sweep.Select(m => m.Topic));
        Assert.True(((JsonObject)JsonNode.Parse(pass.Document)!["cmps"]!["vm_beta"]!).Count == 1);
    }

    [Fact]
    public void AWithheldEntityIsEvictedExactlyAsARemovedOneIs()
    {
        // A group switched off and an Include gone false leave the same thing behind as a deletion.
        var pass = Announce(LedgerWith(Recorded("cpu_load"), Recorded("gpu_load")), [Sample.Sensor("cpu_load")]);

        Assert.Contains(Sample.State("gpu_load"), pass.Sweep.Select(m => m.Topic));
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
        Assert.Null(pass.Ledger.Find(DiscoveryTopics.Device(Sample.Prefix, "exampleapp_old")));
    }

    [Fact]
    public void ADiscoveryPrefixThatMovedIsADifferentIdentity()
    {
        var pass = Announce(
            LedgerWith(Recorded("cpu_load")),
            [Sample.Sensor()],
            identity: new MqttDeviceIdentity(Sample.DeviceId, "ha", Sample.DeviceName));

        Assert.Contains(Sample.ConfigTopic, pass.Evictions.Select(m => m.Topic));
        Assert.Equal(DiscoveryTopics.Device("ha", Sample.DeviceId), pass.ConfigTopic);
    }

    [Fact]
    public void RetiredEntitiesAreEmptiedAtTheirOwnPerComponentPath()
    {
        var pass = Announce(
            new DiscoveryLedger(), [Sample.Sensor()], [new RetiredEntity("sensor", "old_name")]);

        Assert.Equal(
            [DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "old_name")],
            pass.Evictions.Select(m => m.Topic));
    }

    [Fact]
    public void RetiredEntitiesAreLeftAloneWhenThePassIsNotAConnect()
    {
        var pass = Announce(
            new DiscoveryLedger(), [Sample.Sensor()], [new RetiredEntity("sensor", "old_name")],
            includeRetired: false);

        Assert.Empty(pass.Evictions);
    }

    [Fact]
    public void WithdrawEmptiesEverythingTheRecordSaysTheIdentityOwns()
    {
        var (messages, ledger) = DiscoveryPlan.Withdraw(
            LedgerWith(Recorded("cpu_load"), Recorded("restart", "button")),
            Sample.Prefix, Sample.DeviceId, [new RetiredEntity("sensor", "old_name")]);

        Assert.Equal(
            [
                Sample.ConfigTopic,
                Sample.Availability,
                Sample.State("cpu_load"),
                Sample.State("restart"),
                DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "old_name"),
            ],
            messages.Select(m => m.Topic));
        Assert.All(messages, m => Assert.Equal("", m.Payload));
        Assert.Empty(ledger.Devices);
    }

    [Fact]
    public void WithdrawEmptiesTheDocumentEvenWithNoRecordToGoOn()
    {
        // A first run, or a ledger that was lost: one publish removes whatever an earlier
        // installation left at that address.
        var (messages, _) = DiscoveryPlan.Withdraw(new DiscoveryLedger(), Sample.Prefix, Sample.DeviceId, []);

        Assert.Equal([Sample.ConfigTopic], messages.Select(m => m.Topic));
    }

    [Fact]
    public void ThePassLeavesTheLedgerItWasGivenAlone()
    {
        // It is written down only once the messages have landed, so a half-landed pass must not have
        // mutated what the next connect reconciles against.
        var ledger = LedgerWith(Recorded("gone"));

        _ = Announce(ledger, [Sample.Sensor()]);

        Assert.Equal(["gone"], ledger.Find(Sample.ConfigTopic)!.Entities.Select(e => e.EntityId));
    }

    [Fact]
    public void TheAnnouncedDocumentDescribesWhatWasPublished()
    {
        var pass = Announce(new DiscoveryLedger(), [Sample.Sensor(), Sample.Switch()]);
        var components = (JsonObject)JsonNode.Parse(pass.Document)!["cmps"]!;

        Assert.Equal(["cpu_load", "quiet_mode"], components.Select(pair => pair.Key));
    }
}
