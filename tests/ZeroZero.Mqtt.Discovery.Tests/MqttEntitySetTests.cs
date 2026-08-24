using Xunit;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The set, and the projections every pass is built from. An id that collides or that a
/// topic cannot carry is rejected where it is declared rather than discovered on the wire.</summary>
public class MqttEntitySetTests
{
    private static PublishGroupSnapshot Groups(params PublishGroup[] declared) =>
        new PublishGroupSet(new MemorySettingsStore(), declared).Snapshot();

    [Fact]
    public void ADuplicateIdIsRejected()
    {
        // Two entities sharing an id share one unique_id: the second would replace the first in the
        // receiver's registry and take the first's commands with it.
        var error = Assert.Throws<ArgumentException>(() =>
            new MqttEntitySet([Sample.Sensor("cpu_load"), Sample.Switch("cpu_load")]));

        Assert.Contains("cpu_load", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateIdIsRejectedEvenBetweenTwoOfTheSameComponent() =>
        Assert.Throws<ArgumentException>(() =>
            new MqttEntitySet([Sample.Sensor("cpu_load"), Sample.Sensor("cpu_load")]));

    [Theory]
    [InlineData("CPU Load")]
    [InlineData("web server (2)")]
    [InlineData("vm/one")]
    [InlineData("vm+one")]
    [InlineData("")]
    public void AnIdATopicCannotCarryIsRejected(string id) =>
        Assert.Throws<ArgumentException>(() => new MqttEntitySet([Sample.Sensor(id)]));

    [Fact]
    public void TheAvailabilityKeyIsNotAnEntityId() =>
        Assert.Throws<ArgumentException>(() =>
            new MqttEntitySet([Sample.Sensor(MqttTopics.AvailabilityKey)]));

    [Fact]
    public void AnAllocatedIdIsAccepted()
    {
        // What a consumer composing ids from names the machine supplies has to use.
        var allocator = new MqttEntityIdAllocator();
        var set = new MqttEntitySet(
            [Sample.Sensor(allocator.Allocate("Web server (2)")), Sample.Sensor(allocator.Allocate("Web server 2"))]);

        Assert.Equal(["web_server_2", "web_server_2_2"], set.All.Select(e => e.EntityId));
    }

    [Fact]
    public void ANumberWithInvertedBoundsIsRejected()
    {
        var number = new MqttNumber
        {
            EntityId = "poll",
            Name = "Poll",
            Read = () => 1,
            Apply = _ => MqttCommandVerdict.Accept(() => { }),
            Min = 10,
            Max = 1,
        };

        Assert.Throws<ArgumentException>(() => new MqttEntitySet([number]));
    }

    private static MqttNumber Number(double step) => new()
    {
        EntityId = "level",
        Name = "Level",
        Read = () => 1,
        Apply = _ => MqttCommandVerdict.Accept(() => { }),
        Min = 0,
        Max = 1,
        Step = step,
    };

    [Theory]
    [InlineData(0.0001)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void ANumberWithAStepBelowTheSchemasFloorIsRejected(double step)
    {
        // The receiver's schema requires it, and a bad step drops that component from the document
        // silently at the far end — nothing local shows anything wrong.
        Assert.Throws<ArgumentException>(() => new MqttEntitySet([Number(step)]));
    }

    [Fact]
    public void ANumberAtTheSchemasFloorIsAccepted() =>
        Assert.Single(new MqttEntitySet([Number(MqttNumber.MinimumStep)]).All);

    [Fact]
    public void PublishedAndWithheldAreComplements()
    {
        var set = new MqttEntitySet(
            [Sample.Sensor("cpu_load"), Sample.Sensor("gpu_load", group: "metrics"), Sample.Button()]);
        var groups = Groups(new PublishGroup("metrics", "Metrics", DefaultOn: false));

        Assert.Equal(["cpu_load", "restart"], set.Published(groups).Select(e => e.EntityId));
        Assert.Equal(["gpu_load"], set.Withheld(groups).Select(e => e.EntityId));
    }

    [Fact]
    public void AButtonContributesNoChannel()
    {
        // It has nothing to report between presses, so a state topic would carry an empty retained
        // payload nothing ever reads.
        var set = new MqttEntitySet([Sample.Sensor(), Sample.Button()]);
        var channels = MqttEntitySet.Channels(set.Published(null));

        Assert.Equal(["cpu_load"], channels.Select(c => c.Key));
    }

    [Fact]
    public void AChannelReadsItsOwnEntity()
    {
        string? reading = "12";
        var set = new MqttEntitySet([Sample.Sensor(value: reading)]);
        var channel = MqttEntitySet.Channels(set.Published(null))[0];

        Assert.Equal("12", channel.Payload());
        Assert.True(channel.Retain);
    }

    [Fact]
    public void AChannelCarriesTheEntitysDebounce()
    {
        var sensor = new MqttSensor
        {
            EntityId = "cpu_load",
            Name = "CPU load",
            Read = () => "1",
            Debounce = TimeSpan.FromMilliseconds(250),
        };

        Assert.Equal(TimeSpan.FromMilliseconds(250), MqttEntitySet.Channels([sensor])[0].Debounce);
    }

    [Fact]
    public void OnlyCommandEntitiesBecomeTargets()
    {
        var set = new MqttEntitySet(
            [Sample.Sensor(), Sample.BinarySensor(), Sample.Switch(), Sample.Button()]);

        Assert.Equal(
            ["quiet_mode", "restart"],
            MqttEntitySet.CommandTargets(set.Published(null)).Select(t => t.EntityId));
    }

    [Fact]
    public void AWithheldEntityRoutesNowhere()
    {
        // Its component is not announced, so a command addressed to it is reported as unrecognised
        // rather than quietly acted on.
        var set = new MqttEntitySet([Sample.Switch("quiet_mode")]);
        var off = Groups(new PublishGroup("comfort", "Comfort", DefaultOn: false));
        var withheld = new MqttEntitySet(
            [new MqttSwitch
            {
                EntityId = "quiet_mode",
                Name = "Quiet mode",
                Group = "comfort",
                Read = () => true,
                Apply = _ => MqttCommandVerdict.Accept(() => { }),
            }]);

        Assert.Single(MqttEntitySet.CommandTargets(set.Published(off)));
        Assert.Empty(MqttEntitySet.CommandTargets(withheld.Published(off)));
    }

    [Fact]
    public void FindAndNameOfAnswerFromTheDeclaration()
    {
        var set = new MqttEntitySet([Sample.Sensor()]);

        Assert.NotNull(set.Find("cpu_load"));
        Assert.Null(set.Find("nothing"));
        Assert.Equal("CPU load", set.NameOf("cpu_load"));
        Assert.Equal("nothing", set.NameOf("nothing"));
    }

    [Fact]
    public void AnEmptySetIsALegitimateState() => Assert.Empty(MqttEntitySet.Empty.All);
}
