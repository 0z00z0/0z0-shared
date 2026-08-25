using System.Text.Json.Nodes;
using Xunit;
using ZeroZero.Mqtt.Tests;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The whole stack against a real listener speaking MQTT: a connection with the publisher as
/// its listener, a fixed entity table, and a broker that records what arrived. What this proves is
/// the seam — that the layer above announces before anything is declared online, that a value lands
/// on its own bare topic, and that a command sent to the wire reaches the entity that owns it.</summary>
public class DiscoveryLoopbackTests
{
    private const string Root = "exampleapp";
    private const string Device = "desk01";

    private static string ConfigTopic => DiscoveryTopics.Device(MqttSettings.DefaultDiscoveryPrefix, Device);

    private static string State(string entityId) => MqttTopics.Channel(Root, Device, entityId);

    private static string Availability => MqttTopics.Availability(Root, Device);

    /// <summary>A connection and a publisher tied to each other, as a consumer would wire them.</summary>
    private static async Task<(MqttConnection Connection, DiscoveryPublisher Publisher)> ConnectAsync(
        FakeBroker broker, MqttEntitySet entities, IDiscoveryLedgerStore? ledger = null)
    {
        MqttConnection? connection = null;

        var publisher = new DiscoveryPublisher(new DiscoveryPublisherSetup
        {
            IsConnected = () => connection?.IsConnected ?? false,
            TopicRoot = Root,
            Device = new DiscoveryDevice("Example Vendor", "Example App", "1.4.0"),
            Origin = new DiscoveryOrigin("Example App", "1.4.0"),
            Entities = entities,
            Ledger = ledger ?? new TransientLedgerStore(),
            Groups = null,
            SetChannelsAsync = (channels, ct) => connection!.SetChannelsAsync(channels, ct),
            SetCommandTargets = targets => connection!.SetCommandTargets(targets),
        });

        connection = new MqttConnection(new MqttConnectionSetup
        {
            TopicRoot = Root,
            Channels = publisher.Channels(),
            CommandTargets = publisher.CommandTargets(),
            Listener = publisher,
        });

        await connection.ApplyAsync(new MqttConnectParameters
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = broker.Port,
            TransportMode = MqttTransportMode.Tcp,
            EncryptionMode = MqttEncryptionMode.Off,
            DeviceId = Device,
        });

        Assert.True(await FakeBroker.WaitAsync(() => connection.IsConnected), "the connection never came up");
        return (connection, publisher);
    }

    private static JsonObject Components(FakeBroker broker) =>
        (JsonObject)JsonNode.Parse(broker.LastPayload(ConfigTopic)!)!["cmps"]!;

    [Fact]
    public async Task TheDeviceIsAnnouncedBeforeItIsDeclaredOnline()
    {
        // Nothing may be announced as online before the thing being announced exists.
        using var broker = new FakeBroker();
        var (connection, publisher) = await ConnectAsync(
            broker, new MqttEntitySet([Sample.Sensor(), Sample.Switch(), Sample.Button()]));
        using (connection) using (publisher)
        {
            Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(Availability) == "online"));

            var order = broker.Published.Select(p => p.Topic).ToList();
            Assert.True(order.IndexOf(ConfigTopic) >= 0, "the document never reached the broker");
            Assert.True(order.IndexOf(ConfigTopic) < order.IndexOf(Availability));
        }
    }

    [Fact]
    public async Task TheDocumentArrivesRetainedAndDescribesEveryAnnouncedEntity()
    {
        using var broker = new FakeBroker();
        var (connection, publisher) = await ConnectAsync(
            broker, new MqttEntitySet([Sample.Sensor(), Sample.Switch(), Sample.Button()]));
        using (connection) using (publisher)
        {
            Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(ConfigTopic) is not null));

            Assert.True(broker.Published.Single(p => p.Topic == ConfigTopic).Retained);
            Assert.Equal(["cpu_load", "quiet_mode", "restart"], Components(broker).Select(p => p.Key));
        }
    }

    [Fact]
    public async Task EveryEntityGetsItsOwnBareTopicCarryingAPlainValue()
    {
        using var broker = new FakeBroker();
        var (connection, publisher) = await ConnectAsync(broker, new MqttEntitySet(
        [
            Sample.Sensor(value: "42"),
            Sample.Switch(read: () => true),
            Sample.Number(read: () => 12.5),
            Sample.Select(read: () => "Home"),
            Sample.Button(),
        ]));
        using (connection) using (publisher)
        {
            Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(State("profile")) is not null));

            Assert.Equal("42", broker.LastPayload(State("cpu_load")));
            Assert.Equal("ON", broker.LastPayload(State("quiet_mode")));
            Assert.Equal("12.5", broker.LastPayload(State("poll_interval")));
            Assert.Equal("Home", broker.LastPayload(State("profile")));

            // A button is command-only: nothing is published on its behalf at all.
            Assert.Equal(0, broker.CountOn(State("restart")));
        }
    }

    [Fact]
    public async Task AnInboundCommandReachesTheEntityThatOwnsIt()
    {
        bool? applied = null;
        using var broker = new FakeBroker();
        var (connection, publisher) = await ConnectAsync(
            broker, new MqttEntitySet([Sample.Switch(apply: on => applied = on)]));
        using (connection) using (publisher)
        {
            Assert.True(await FakeBroker.WaitAsync(
                () => broker.Subscriptions.Contains(MqttTopics.CommandFilter(Root, Device))));

            await broker.SendAsync(MqttTopics.Command(Root, Device, "quiet_mode"), "OFF");

            Assert.True(await FakeBroker.WaitAsync(() => applied is not null));
            Assert.False(applied);
        }
    }

    [Fact]
    public async Task AnEntitySetReplacedAtRuntimeMovesTheDocumentAndTheTopicsTogether()
    {
        using var broker = new FakeBroker();
        var (connection, publisher) = await ConnectAsync(
            broker, new MqttEntitySet([Sample.Sensor("vm_alpha"), Sample.Sensor("vm_beta")]));
        using (connection) using (publisher)
        {
            Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(State("vm_beta")) is not null));

            await publisher.SetEntitiesAsync(new MqttEntitySet([Sample.Sensor("vm_alpha")]));

            Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(State("vm_beta")) == ""));
            Assert.Equal(["p"], ((JsonObject)Components(broker)["vm_beta"]!).Select(p => p.Key));
        }
    }

    [Fact]
    public async Task SwitchingPublishingOffLeavesTheDeviceStandingAsOffline()
    {
        // A settings switch, not a deletion. Removing the device here would take it off the receiver
        // altogether on an action the user reads as "pause".
        using var broker = new FakeBroker();
        var (connection, publisher) = await ConnectAsync(
            broker, new MqttEntitySet([Sample.Sensor(), Sample.Button()]));
        using (publisher)
        {
            Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(State("cpu_load")) == "12"));
            string document = broker.LastPayload(ConfigTopic)!;

            await connection.ApplyAsync(new MqttConnectParameters { Enabled = false });
            connection.Dispose();

            Assert.Equal(document, broker.LastPayload(ConfigTopic));
            Assert.Equal("12", broker.LastPayload(State("cpu_load")));
            Assert.Equal("offline", broker.LastPayload(Availability));
        }
    }

    [Fact]
    public async Task RemovingTheDeviceLeavesNothingRetained()
    {
        using var broker = new FakeBroker();
        var (connection, publisher) = await ConnectAsync(
            broker, new MqttEntitySet([Sample.Sensor(), Sample.Button()]));
        using (publisher)
        {
            Assert.True(await FakeBroker.WaitAsync(() => broker.LastPayload(State("cpu_load")) == "12"));

            Assert.True(await connection.RemoveDeviceAsync());
            connection.Dispose();

            Assert.Equal("", broker.LastPayload(ConfigTopic));
            Assert.Equal("", broker.LastPayload(State("cpu_load")));
            Assert.Equal("", broker.LastPayload(Availability));
            Assert.Equal("", broker.LastPayload(MqttTopics.Command(Root, Device, "restart")));
        }
    }
}
