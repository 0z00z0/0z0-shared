using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The composed document, against a fixed device id. Golden in the sense that matters: every
/// <c>unique_id</c>, every state topic and every command topic is asserted, because identity lives in
/// <c>unique_id</c> and a silent change to one would move every entity in every installation.</summary>
public class DiscoveryDocumentTests
{
    private static JsonObject Build(
        IReadOnlyList<MqttEntity> components, IReadOnlyList<PublishedEntity>? removed = null) =>
        (JsonObject)JsonNode.Parse(DiscoveryDocument.Build(
            Sample.TopicRoot, Sample.Identity, Sample.Device, Sample.Origin, components, removed ?? []))!;

    private static JsonObject Component(JsonObject document, string entityId) =>
        (JsonObject)document["cmps"]![entityId]!;

    private static IEnumerable<string> EveryKey(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(pair => EveryKey(pair.Value).Prepend(pair.Key)),
        JsonArray a => a.SelectMany(EveryKey),
        _ => [],
    };

    [Fact]
    public void TheRootCarriesTheDeviceTheOriginAndAvailability()
    {
        var document = Build([Sample.Sensor()]);

        Assert.Equal(
            ["dev", "o", "availability_topic", "payload_available", "payload_not_available", "qos", "cmps"],
            document.Select(pair => pair.Key));
    }

    [Fact]
    public void AvailabilityIsPublishedOnceAtTheRoot()
    {
        var document = Build([Sample.Sensor(), Sample.Switch(), Sample.Button()]);

        Assert.Equal(Sample.Availability, (string?)document["availability_topic"]);
        Assert.Equal("online", (string?)document["payload_available"]);
        Assert.Equal("offline", (string?)document["payload_not_available"]);

        // No component repeats it: the document says it once and the components inherit.
        Assert.Equal(1, EveryKey(document).Count(k => k == "availability_topic"));
        Assert.DoesNotContain("availability", EveryKey(document));
    }

    [Fact]
    public void TheDeviceBlockIsTheIdentityInForcePlusWhatDoesNotVary()
    {
        var device = (JsonObject)Build([Sample.Sensor()])["dev"]!;

        Assert.Equal([Sample.DeviceId], device["ids"]!.AsArray().Select(n => (string?)n));
        Assert.Equal(Sample.DeviceName, (string?)device["name"]);
        Assert.Equal("Example Vendor", (string?)device["mf"]);
        Assert.Equal("Example App", (string?)device["mdl"]);
        Assert.Equal("1.4.0", (string?)device["sw"]);
        Assert.Equal("https://example.invalid/app", (string?)device["cu"]);
    }

    [Fact]
    public void AConfigurationUrlIsOmittedRatherThanWrittenEmpty()
    {
        var json = DiscoveryDocument.Build(
            Sample.TopicRoot, Sample.Identity, new DiscoveryDevice("V", "M", "1.0"), Sample.Origin,
            [Sample.Sensor()], []);
        var device = (JsonObject)JsonNode.Parse(json)!["dev"]!;

        Assert.False(device.ContainsKey("cu"));
    }

    [Fact]
    public void TheOriginBlockSaysWhatProducedTheDocument()
    {
        var origin = (JsonObject)Build([Sample.Sensor()])["o"]!;

        Assert.Equal("Example App", (string?)origin["name"]);
        Assert.Equal("1.4.0", (string?)origin["sw"]);
        Assert.Equal("https://example.invalid/support", (string?)origin["url"]);
    }

    [Fact]
    public void ASupportUrlIsOmittedRatherThanWrittenEmpty()
    {
        var json = DiscoveryDocument.Build(
            Sample.TopicRoot, Sample.Identity, Sample.Device, new DiscoveryOrigin("App", "1.0"),
            [Sample.Sensor()], []);

        Assert.False(((JsonObject)JsonNode.Parse(json)!["o"]!).ContainsKey("url"));
    }

    [Fact]
    public void ObjectIdIsNeverWritten()
    {
        // Deprecated under device discovery, and it pins an entity id the receiver composes better.
        // default_entity_id would pin the same thing and is not written either.
        var document = Build(
            [Sample.Sensor(), Sample.Switch(), Sample.Number(), Sample.Select(), Sample.Button(),
             Sample.Text(), Sample.BinarySensor()]);

        Assert.DoesNotContain("object_id", EveryKey(document));
        Assert.DoesNotContain("default_entity_id", EveryKey(document));
    }

    [Fact]
    public void UniqueIdIsTheDeviceIdAndTheEntityId()
    {
        var document = Build([Sample.Sensor(), Sample.Switch(), Sample.Button()]);

        Assert.Equal("exampleapp_desk01_cpu_load", (string?)Component(document, "cpu_load")["unique_id"]);
        Assert.Equal("exampleapp_desk01_quiet_mode", (string?)Component(document, "quiet_mode")["unique_id"]);
        Assert.Equal("exampleapp_desk01_restart", (string?)Component(document, "restart")["unique_id"]);
    }

    [Fact]
    public void EveryComponentNamesItsPlatform()
    {
        var document = Build([Sample.Sensor(), Sample.Select(), Sample.Button()]);

        Assert.Equal("sensor", (string?)Component(document, "cpu_load")["p"]);
        Assert.Equal("select", (string?)Component(document, "profile")["p"]);
        Assert.Equal("button", (string?)Component(document, "restart")["p"]);
    }

    [Fact]
    public void AStateTopicIsOneBareTopicPerEntity()
    {
        var document = Build([Sample.Sensor(), Sample.Switch()]);

        Assert.Equal(
            "exampleapp/exampleapp_desk01/cpu_load",
            (string?)Component(document, "cpu_load")["state_topic"]);
        Assert.Equal(
            "exampleapp/exampleapp_desk01/quiet_mode",
            (string?)Component(document, "quiet_mode")["state_topic"]);
    }

    [Fact]
    public void NoComponentCarriesAValueTemplate()
    {
        // A plain value on a bare topic is the whole point: a shell script reads it with no parsing.
        var document = Build(
            [Sample.Sensor(), Sample.Switch(), Sample.Number(), Sample.Select(), Sample.Text(),
             Sample.BinarySensor()]);

        Assert.DoesNotContain("value_template", EveryKey(document));
        Assert.DoesNotContain("command_template", EveryKey(document));
    }

    [Fact]
    public void ACommandTopicSitsUnderTheCommandSegment()
    {
        var document = Build([Sample.Switch(), Sample.Button()]);

        Assert.Equal(
            "exampleapp/exampleapp_desk01/cmd/quiet_mode",
            (string?)Component(document, "quiet_mode")["command_topic"]);
        Assert.Equal(
            "exampleapp/exampleapp_desk01/cmd/restart",
            (string?)Component(document, "restart")["command_topic"]);
    }

    [Fact]
    public void AReadOnlyEntityGetsNoCommandTopic()
    {
        var document = Build([Sample.Sensor(), Sample.BinarySensor()]);

        Assert.False(Component(document, "cpu_load").ContainsKey("command_topic"));
        Assert.False(Component(document, "charging").ContainsKey("command_topic"));
    }

    [Fact]
    public void AButtonDeclaresNoStateChannel()
    {
        var button = Component(Build([Sample.Button()]), "restart");

        Assert.False(button.ContainsKey("state_topic"));
        Assert.Equal("exampleapp/exampleapp_desk01/cmd/restart", (string?)button["command_topic"]);
        Assert.Equal("PRESS", (string?)button["payload_press"]);
    }

    [Fact]
    public void PrimaryWritesNoEntityCategory()
    {
        // It is a value, not a gap: it keeps the control on the main card.
        var document = Build([Sample.Sensor(), Sample.Number()]);

        Assert.False(Component(document, "cpu_load").ContainsKey("entity_category"));
        Assert.Equal("config", (string?)Component(document, "poll_interval")["entity_category"]);
    }

    [Fact]
    public void ADiagnosticEntitySaysSo()
    {
        var sensor = new MqttSensor
        {
            EntityId = "uptime",
            Name = "Uptime",
            Read = () => "1d",
            Category = MqttEntityCategory.Diagnostic,
        };

        Assert.Equal("diagnostic", (string?)Component(Build([sensor]), "uptime")["entity_category"]);
    }

    [Fact]
    public void ASensorCarriesItsUnitAndStateClass()
    {
        var sensor = Component(Build([Sample.Sensor()]), "cpu_load");

        Assert.Equal("%", (string?)sensor["unit_of_measurement"]);
        Assert.Equal("measurement", (string?)sensor["state_class"]);
    }

    [Fact]
    public void ASensorWithNothingToDeclareDeclaresNothing()
    {
        var sensor = new MqttSensor { EntityId = "note", Name = "Note", Read = () => "x" };
        var entry = Component(Build([sensor]), "note");

        Assert.Equal(["p", "unique_id", "name", "state_topic"], entry.Select(pair => pair.Key));
    }

    [Fact]
    public void ABinarySensorCarriesItsPairAndItsDeviceClass()
    {
        var entry = Component(Build([Sample.BinarySensor()]), "charging");

        Assert.Equal("battery_charging", (string?)entry["device_class"]);
        Assert.Equal("ON", (string?)entry["payload_on"]);
        Assert.Equal("OFF", (string?)entry["payload_off"]);
    }

    [Fact]
    public void ANumberCarriesItsBoundsAsNumbers()
    {
        var entry = Component(Build([Sample.Number()]), "poll_interval");

        Assert.Equal(5, (double?)entry["min"]);
        Assert.Equal(300, (double?)entry["max"]);
        Assert.Equal(5, (double?)entry["step"]);
        Assert.Equal("s", (string?)entry["unit_of_measurement"]);
        Assert.Equal("slider", (string?)entry["mode"]);
    }

    [Fact]
    public void ANumbersBoundsAreWrittenForAMachineNotForALocale()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nb-NO");
            var number = new MqttNumber
            {
                EntityId = "level",
                Name = "Level",
                Read = () => 1,
                Apply = _ => MqttCommandVerdict.Accept(() => { }),
                Min = 0.5,
                Max = 99.5,
                Step = 0.5,
            };

            string json = DiscoveryDocument.Build(
                Sample.TopicRoot, Sample.Identity, Sample.Device, Sample.Origin, [number], []);

            Assert.Contains("\"min\":0.5", json, StringComparison.Ordinal);
            Assert.DoesNotContain("0,5", json, StringComparison.Ordinal);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void ASelectPublishesItsOptionsAndItsSentinel()
    {
        var entry = Component(Build([Sample.Select()]), "profile");

        Assert.Equal(
            ["Office", "Home", MqttSelect.DefaultNoOption],
            entry["options"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public void ASelectsOptionsAreReadWhenTheDocumentIsComposed()
    {
        List<string> options = ["Office"];
        var select = Sample.Select(options: () => options);

        Assert.Contains("Office", Component(Build([select]), "profile")["options"]!.AsArray().Select(n => (string?)n));

        options[0] = "Studio";
        Assert.Contains("Studio", Component(Build([select]), "profile")["options"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public void ASwitchDeclaresThePairItAcceptsAndPublishes()
    {
        var entry = Component(Build([Sample.Switch()]), "quiet_mode");

        Assert.Equal("ON", (string?)entry["payload_on"]);
        Assert.Equal("OFF", (string?)entry["payload_off"]);
    }

    [Fact]
    public void TextCarriesItsLengthAndItsMode()
    {
        var text = new MqttText
        {
            EntityId = "secret",
            Name = "Secret",
            Read = () => "",
            Apply = _ => MqttCommandVerdict.Accept(() => { }),
            MinLength = 4,
            MaxLength = 32,
            Mode = MqttTextMode.Password,
            Pattern = "[a-z]+",
        };
        var entry = Component(Build([text]), "secret");

        Assert.Equal(4, (int?)entry["min"]);
        Assert.Equal(32, (int?)entry["max"]);
        Assert.Equal("password", (string?)entry["mode"]);
        Assert.Equal("[a-z]+", (string?)entry["pattern"]);
    }

    [Fact]
    public void AnIconIsWrittenWhereOneIsDeclared()
    {
        var sensor = new MqttSensor
        {
            EntityId = "cpu_load", Name = "CPU load", Read = () => "1", Icon = "mdi:chip",
        };

        Assert.Equal("mdi:chip", (string?)Component(Build([sensor]), "cpu_load")["icon"]);
    }

    [Fact]
    public void AnExtraKeyReachesTheComponent()
    {
        var sensor = new MqttSensor
        {
            EntityId = "cpu_load",
            Name = "CPU load",
            Read = () => "1",
            Extra = new Dictionary<string, object?> { ["expire_after"] = 600 },
        };

        Assert.Equal(600, (int?)Component(Build([sensor]), "cpu_load")["expire_after"]);
    }

    [Fact]
    public void AnExtraKeyWinsOverTheOneTheComponentWrote()
    {
        // The escape hatch is last, so a one-off correction does not need a new property first.
        var sensor = new MqttSensor
        {
            EntityId = "cpu_load",
            Name = "CPU load",
            Read = () => "1",
            Unit = "%",
            Extra = new Dictionary<string, object?> { ["unit_of_measurement"] = "percent" },
        };

        Assert.Equal("percent", (string?)Component(Build([sensor]), "cpu_load")["unit_of_measurement"]);
    }

    [Fact]
    public void ARemovedComponentCarriesOnlyItsPlatform()
    {
        // Leaving it out of a later document does not remove it — the receiver keeps what it has —
        // so removal is something the document says.
        var document = Build(
            [Sample.Sensor()],
            [new PublishedEntity { EntityId = "gone", Platform = "switch", StateTopic = Sample.State("gone") }]);
        var stub = Component(document, "gone");

        Assert.Equal(["p"], stub.Select(pair => pair.Key));
        Assert.Equal("switch", (string?)stub["p"]);
    }

    [Fact]
    public void ADeviceWithNothingToAnnounceStillComposes()
    {
        var document = Build([]);

        Assert.Empty((JsonObject)document["cmps"]!);
    }

    [Fact]
    public void TheDocumentIsCompactAndUnescaped()
    {
        var identity = new MqttDeviceIdentity(Sample.DeviceId, Sample.Prefix, "Kjøkken");
        string json = DiscoveryDocument.Build(
            Sample.TopicRoot, identity, Sample.Device, Sample.Origin, [Sample.Sensor()], []);

        Assert.Contains("Kjøkken", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", json, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(json).RootElement.ValueKind);
    }

    [Fact]
    public void TheQosTheReceiverSubscribesAtIsDeclared() =>
        Assert.Equal(1, (int?)Build([Sample.Sensor()])["qos"]);
}
