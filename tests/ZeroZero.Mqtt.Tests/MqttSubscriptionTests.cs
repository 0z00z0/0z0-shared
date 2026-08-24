using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>A client subscribed to several filters is handed every message on one callback with
/// nothing saying which filter brought it, so the module matches topics itself.</summary>
public class MqttSubscriptionTests
{
    [Theory]
    [InlineData("homeassistant/status", "homeassistant/status")]
    [InlineData("app/+/state", "app/desk01/state")]
    [InlineData("app/#", "app/desk01/cmd/quiet_mode")]
    [InlineData("app/desk01/cmd/#", "app/desk01/cmd/quiet_mode")]
    [InlineData("#", "anything/at/all")]
    public void MatchesFilter_AcceptsATopicTheFilterCovers(string filter, string topic) =>
        Assert.True(MqttSubscription.MatchesFilter(filter, topic));

    [Theory]
    [InlineData("homeassistant/status", "homeassistant/statuses")]
    [InlineData("app/+/state", "app/desk01/sub/state")]
    [InlineData("app/+/state", "app/state")]
    [InlineData("app/desk01/cmd/#", "app/desk01/state")]
    [InlineData("app/desk01/state", "app/desk01/state/extra")]
    public void MatchesFilter_RejectsATopicTheFilterDoesNotCover(string filter, string topic) =>
        Assert.False(MqttSubscription.MatchesFilter(filter, topic));

    [Fact]
    public void MatchesFilter_IsCaseSensitiveAsTheProtocolIs() =>
        Assert.False(MqttSubscription.MatchesFilter("homeassistant/status", "HomeAssistant/status"));

    [Fact]
    public void ASubscriptionMatchesThroughItsOwnFilter()
    {
        var subscription = new MqttSubscription("homeassistant/status", (_, _) => Task.CompletedTask);

        Assert.True(subscription.Matches("homeassistant/status"));
        Assert.False(subscription.Matches("homeassistant/other"));
    }

    [Fact]
    public void ASubscriptionDefaultsToTheQosEverythingElseUses() =>
        Assert.Equal(MqttQos.AtLeastOnce,
            new MqttSubscription("t", (_, _) => Task.CompletedTask).Qos);
}
