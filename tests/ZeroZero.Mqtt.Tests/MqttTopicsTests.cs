using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>Every topic the module publishes on is composed in one place, so what is published and
/// what is subscribed cannot drift apart.</summary>
public class MqttTopicsTests
{
    private const string Root = "exampleapp";
    private const string Device = "exampleapp_desk01";

    [Fact]
    public void Channel_IsRootThenDeviceThenKey() =>
        Assert.Equal("exampleapp/exampleapp_desk01/cpu_load", MqttTopics.Channel(Root, Device, "cpu_load"));

    [Fact]
    public void Availability_IsAChannelOnTheReservedKey() =>
        Assert.Equal("exampleapp/exampleapp_desk01/availability", MqttTopics.Availability(Root, Device));

    [Fact]
    public void Command_SitsUnderItsOwnSegment() =>
        Assert.Equal("exampleapp/exampleapp_desk01/cmd/quiet_mode",
            MqttTopics.Command(Root, Device, "quiet_mode"));

    [Fact]
    public void CommandFilter_CoversEveryCommandEntityAndOnlyThose()
    {
        string filter = MqttTopics.CommandFilter(Root, Device);

        Assert.Equal("exampleapp/exampleapp_desk01/cmd/#", filter);
        Assert.True(MqttSubscription.MatchesFilter(filter, MqttTopics.Command(Root, Device, "quiet_mode")));
        Assert.False(MqttSubscription.MatchesFilter(filter, MqttTopics.Channel(Root, Device, "quiet_mode")));
    }

    [Fact]
    public void CommandEntityId_ReadsTheSuffixBack() =>
        Assert.Equal("quiet_mode",
            MqttTopics.CommandEntityId(Root, Device, "exampleapp/exampleapp_desk01/cmd/quiet_mode"));

    [Theory]
    [InlineData("exampleapp/exampleapp_desk01/cpu_load")]
    [InlineData("exampleapp/exampleapp_desk01/cmd/")]
    [InlineData("other/exampleapp_desk01/cmd/quiet_mode")]
    [InlineData("exampleapp/other_device/cmd/quiet_mode")]
    public void CommandEntityId_IsNullForAnythingThatIsNotACommandTopic(string topic) =>
        Assert.Null(MqttTopics.CommandEntityId(Root, Device, topic));

    [Theory]
    [InlineData("")]
    [InlineData(MqttTopics.AvailabilityKey)]
    [InlineData("a/b")]
    [InlineData("a+b")]
    [InlineData("a#")]
    public void ValidateChannelKey_RejectsAKeyThatWouldPublishSomewhereElse(string key) =>
        Assert.NotNull(MqttTopics.ValidateChannelKey(key));

    [Fact]
    public void ValidateChannelKey_AcceptsAnOrdinaryEntityId() =>
        Assert.Null(MqttTopics.ValidateChannelKey("cpu_load"));
}
