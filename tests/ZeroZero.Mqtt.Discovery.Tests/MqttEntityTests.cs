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
    public void AnAbsentReadingIsPublishedAsTheResetLiteralNotAsAnEmptyPayload()
    {
        // A receiver ignores a zero-length payload on all five and goes on showing the last value it
        // saw, which is exactly the stale state the rule exists to prevent. What it reads as "no
        // value" is the literal.
        Assert.Equal("None", Sample.Sensor(value: null).ReadState());
        Assert.Equal("None", Sample.BinarySensor(read: () => null).ReadState());
        Assert.Equal("None", Sample.Switch(read: () => null).ReadState());
        Assert.Equal("None", Sample.Number(read: () => null).ReadState());
        Assert.Equal("None", Sample.Select(read: () => null).ReadState());
    }

    [Fact]
    public void TextIsTheOnePlatformThatEmptiesItsTopic()
    {
        // An empty string is a legitimate value on a text entity, so the two are the same bytes and
        // the literal would be stored as the word rather than read as "no value".
        Assert.Null(Sample.Text(read: () => null).ReadState());
        Assert.False(Sample.Text().AlwaysCarriesValue);
    }

    [Fact]
    public void ATextValuedSensorReadingTheResetLiteralIsIndistinguishableFromNoReading()
    {
        // The collision is unavoidable: the receiver reserves the literal and offers no second form.
        // Recorded so nobody later reads it as a bug in the reader.
        Assert.Equal(Sample.Sensor(value: null).ReadState(), Sample.Sensor(value: "None").ReadState());
    }

    [Fact]
    public void ASensorPublishesItsReading()
    {
        Assert.Equal("12", Sample.Sensor(value: "12").ReadState());
        Assert.Equal(MqttPayload.None, Sample.Sensor(value: null).ReadState());
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
    public void AReadingThatGoesAbsentResetsTheValueAgain()
    {
        string? reading = "12";
        var sensor = new MqttSensor { EntityId = "cpu_load", Name = "CPU load", Read = () => reading };

        Assert.Equal("12", sensor.ReadState());
        reading = null;
        Assert.Equal(MqttPayload.None, sensor.ReadState());
    }

    [Fact]
    public void ABinarySensorPublishesTheDeclaredPair()
    {
        Assert.Equal("ON", Sample.BinarySensor(read: () => true).ReadState());
        Assert.Equal("OFF", Sample.BinarySensor(read: () => false).ReadState());
        Assert.Equal(MqttPayload.None, Sample.BinarySensor(read: () => null).ReadState());
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
        Assert.Equal(MqttPayload.None, Sample.Number(read: () => null).ReadState());
    }

    [Fact]
    public void ASelectResetsRatherThanEmptyingItsTopic()
    {
        var select = Sample.Select(read: () => null);

        Assert.Equal(MqttPayload.None, select.ReadState());
        Assert.NotEqual("", select.ReadState());
    }

    [Fact]
    public void ASelectWithNothingToOfferOffersNothing()
    {
        // No sentinel keeping the list non-empty: the reset literal works without being an option, so
        // an empty list is simply an empty list.
        Assert.Empty(Sample.Select(options: () => []).Options());
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

        Assert.Contains("Office", select.Options());

        options = ["Home"];
        Assert.Contains("Home", select.Options());
        Assert.DoesNotContain("Office", select.Options());
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

    [Fact]
    public void AnIncludeThatThrowsIsUnknownRatherThanFalse()
    {
        // It reads live hardware, and every way that read can fail returns the same answer as "this
        // capability is absent" to a predicate that can only say true or false. A resume from standby
        // forces a reconnect at exactly the moment those reads are least likely to answer.
        var entity = Sample.Sensor(include: () => throw new TimeoutException("the controller is busy"));

        Assert.Null(entity.IsPublished(null));
    }

    [Fact]
    public void ASwitchedOffGroupIsFalseWithoutTheCapabilityBeingReadAtAll()
    {
        // The user's decision settles it, and reading hardware for an entity nobody wants is work for
        // its own sake — including work that can throw.
        int reads = 0;
        var groups = new PublishGroupSet(
            new MemorySettingsStore(), [new PublishGroup("metrics", "Metrics", DefaultOn: false)]);
        var entity = Sample.Sensor(group: "metrics", include: () => { reads++; return true; });

        Assert.False(entity.IsPublished(groups.Snapshot()));
        Assert.Equal(0, reads);
    }
}
