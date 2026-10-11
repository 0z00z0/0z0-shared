using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The static face forwards; the wording is pinned in <see cref="MqttPanelTextTests"/>.</summary>
public class MqttStatusTextTests
{
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void Describe_OfASuccessfulRunIsTheOutcomeAndNotTheAttempts()
    {
        MqttProbeReport report = new([
            new(new(1883, MqttTransport.Tcp), MqttProbeOutcome.Unreachable),
            new(new(443, MqttTransport.WebSocket), MqttProbeOutcome.Success),
        ]);

        string sentence = MqttStatusText.Describe(report);

        // A leg that failed on the way to a success reads as a fault to investigate.
        Assert.Equal("Connected over WebSocket on port 443.", sentence);
    }
}
