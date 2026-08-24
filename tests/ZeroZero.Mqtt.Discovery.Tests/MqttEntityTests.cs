using Xunit;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>What each component type is, and what it puts on its topic. The three properties every
/// pass depends on — the platform name, whether there is state at all, and what an absent reading
/// publishes — are declared per type rather than assumed, so each type is asked directly.</summary>
public class MqttEntityTests
{
    [Fact]
    public void EachComponentNamesItsOwnPlatform()
    {
        Assert.Equal("sensor", Sample.Sensor().Platform);
        Assert.Equal("binary_sensor", Sample.BinarySensor().Platform);
        Assert.Equal("switch", Sample.Switch().Platform);
        Assert.Equal("number", Sample.Number().Platform);
        Assert.Equal("select", Sample.Select().Platform);
        Assert.Equal("button", Sample.Button().Platform);
        Assert.Equal("text", Sample.Text().Platform);
    }

    [Fact]
    public void ACommandEntityIsTheOneThatTakesCommands()
    {
        Assert.False(Sample.Sensor().IsCommand);
        Assert.False(Sample.BinarySensor().IsCommand);

        Assert.True(Sample.Switch().IsCommand);
        Assert.True(Sample.Number().IsCommand);
        Assert.True(Sample.Select().IsCommand);
        Assert.True(Sample.Button().IsCommand);
        Assert.True(Sample.Text().IsCommand);
    }

    [Fact]
    public void AButtonIsCommandOnly()
    {
        var button = Sample.Button();

        Assert.False(button.HasState);
        Assert.Null(button.ReadState());
        Assert.True(button.IsCommand);
    }

    [Fact]
    public void EmptyPayloadSemanticsAreDeclaredPerPlatform()
    {
        // Everything whose empty payload a receiver reads as "no value" empties its topic.
        Assert.False(Sample.Sensor().AlwaysCarriesValue);
        Assert.False(Sample.BinarySensor().AlwaysCarriesValue);
        Assert.False(Sample.Switch().AlwaysCarriesValue);
        Assert.False(Sample.Number().AlwaysCarriesValue);
        Assert.False(Sample.Text().AlwaysCarriesValue);

        // A select's empty payload is ignored, so it never sends one.
        Assert.True(Sample.Select().AlwaysCarriesValue);
    }

    [Fact]
    public void ASensorPublishesItsReadingOrEmptiesItsTopic()
    {
        Assert.Equal("12", Sample.Sensor(value: "12").ReadState());
        Assert.Null(Sample.Sensor(value: null).ReadState());
    }

    [Fact]
    public void AReadingIsTakenEveryTimeItIsAsked()
    {
        // Every publish pass reads the entity again. A reading held on to would put a value on the
        // topic that stopped being true and never changed back.
        int reads = 0;
        var sensor = new MqttSensor
        {
            EntityId = "cpu_load",
            Name = "CPU load",
            Read = () => (++reads).ToString(),
        };

        Assert.Equal("1", sensor.ReadState());
        Assert.Equal("2", sensor.ReadState());
        Assert.Equal("3", MqttEntitySet.Channels([sensor])[0].Payload());
    }

    [Fact]
    public void AReadingThatGoesAbsentEmptiesTheTopicAgain()
    {
        string? reading = "12";
        var sensor = new MqttSensor { EntityId = "cpu_load", Name = "CPU load", Read = () => reading };

        Assert.Equal("12", sensor.ReadState());
        reading = null;
        Assert.Null(sensor.ReadState());
    }

    [Fact]
    public void ABinarySensorPublishesTheDeclaredPair()
    {
        Assert.Equal("ON", Sample.BinarySensor(read: () => true).ReadState());
        Assert.Equal("OFF", Sample.BinarySensor(read: () => false).ReadState());
        Assert.Null(Sample.BinarySensor(read: () => null).ReadState());
    }

    [Fact]
    public void ABinarySensorHonoursItsOwnPair()
    {
        var sensor = new MqttBinarySensor
        {
            EntityId = "running",
            Name = "Running",
            Read = () => true,
            PayloadOn = "RUNNING",
            PayloadOff = "STOPPED",
        };

        Assert.Equal("RUNNING", sensor.ReadState());
    }

    [Fact]
    public void ANumberPublishesAMachineReadableValue()
    {
        Assert.Equal("30", Sample.Number(read: () => 30).ReadState());
        Assert.Equal("12.5", Sample.Number(read: () => 12.5).ReadState());
        Assert.Null(Sample.Number(read: () => null).ReadState());
    }

    [Fact]
    public void ASelectNeverPublishesAnEmptyPayload()
    {
        var select = Sample.Select(read: () => null);

        Assert.Equal(MqttSelect.DefaultNoOption, select.ReadState());
        Assert.NotEqual("", select.ReadState());
    }

    [Fact]
    public void ASelectOffersTheSentinelAsAnOption()
    {
        // It is publishable at any moment, and a payload that is not an option is one the receiver
        // discards.
        var select = Sample.Select(options: () => ["Office", "Home"]);

        Assert.Equal(["Office", "Home", MqttSelect.DefaultNoOption], select.PublishedOptions());
    }

    [Fact]
    public void ASelectWithNothingToOfferStillOffersSomething()
    {
        var select = Sample.Select(options: () => []);

        Assert.Equal([MqttSelect.DefaultNoOption], select.PublishedOptions());
    }

    [Fact]
    public void ASelectDoesNotOfferItsSentinelTwice()
    {
        var select = Sample.Select(options: () => ["Office", MqttSelect.DefaultNoOption]);

        Assert.Equal(["Office", MqttSelect.DefaultNoOption], select.PublishedOptions());
    }

    [Fact]
    public void ASelectsOptionsAreReadEveryTimeTheyAreAsked()
    {
        // A fresh list each time, not the same one mutated: a consumer composing its options from
        // what the machine currently holds hands back a new list on every call, and an implementation
        // that held on to the first one would never see a second.
        int reads = 0;
        IReadOnlyList<string> options = ["Office"];
        var select = Sample.Select(options: () => { reads++; return options; });

        Assert.Contains("Office", select.PublishedOptions());

        options = ["Home"];
        Assert.Contains("Home", select.PublishedOptions());
        Assert.DoesNotContain("Office", select.PublishedOptions());
        Assert.Equal(3, reads);
    }

    [Fact]
    public void IncludeAndTheGroupBothDecideMembership()
    {
        var groups = new PublishGroupSet(
            new MemorySettingsStore(), [new PublishGroup("metrics", "Metrics", DefaultOn: false)]);

        Assert.False(Sample.Sensor(group: "metrics").IsPublished(groups.Snapshot()));
        Assert.True(Sample.Sensor(group: null).IsPublished(groups.Snapshot()));
        Assert.False(Sample.Sensor(include: () => false).IsPublished(groups.Snapshot()));
        Assert.True(Sample.Sensor(include: () => true).IsPublished(groups.Snapshot()));
    }

    [Fact]
    public void WithNoGroupsDeclaredEverythingIsPublished() =>
        Assert.True(Sample.Sensor(group: "metrics").IsPublished(null));

    [Fact]
    public void IncludeIsAskedEveryTime()
    {
        bool capable = false;
        var entity = Sample.Sensor(include: () => capable);

        Assert.False(entity.IsPublished(null));
        capable = true;
        Assert.True(entity.IsPublished(null));
    }
}
