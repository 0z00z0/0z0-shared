using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The sentences a settings panel shows. Pure, and the instant is passed in rather than
/// read, so the wording is pinned without a clock.</summary>
public class MqttStatusTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Relative_SaysSoWhenThereIsNothingToRender() =>
        Assert.Equal("never", MqttStatusText.Relative(null, Now, never: "never"));

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3 * 3600, "3 hours ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(2 * 86400, "2 days ago")]
    public void Relative_RendersTheAgeInTheLargestUnitThatFits(int secondsAgo, string expected) =>
        Assert.Equal(expected, MqttStatusText.Relative(Now.AddSeconds(-secondsAgo), Now, "never"));

    [Fact]
    public void Relative_ReadsAFutureStampAsJustNowRatherThanANegativeAge() =>
        Assert.Equal("just now", MqttStatusText.Relative(Now.AddHours(1), Now, "never"));

    [Fact]
    public void DescribeBroker_SaysNotSetWithNoHost() =>
        Assert.Equal("Not set", MqttStatusText.DescribeBroker(
            new("  ", "user", null, MqttTransportMode.Auto), null));

    [Fact]
    public void DescribeBroker_WithdrawsRatherThanShowingHalfAnAnswer()
    {
        var request = new MqttEndpointRequest("broker.invalid", "user", null, MqttTransportMode.Auto);

        Assert.Equal("broker.invalid — not connected yet", MqttStatusText.DescribeBroker(request, null));
    }

    [Fact]
    public void DescribeBroker_NamesThePortAndTransportInForce()
    {
        var request = new MqttEndpointRequest("broker.invalid", "user", null, MqttTransportMode.Auto);
        var memory = new MqttEndpointMemory("broker.invalid", "user", 8883, MqttTransport.Tcp, true);

        Assert.Equal("broker.invalid:8883 over TCP", MqttStatusText.DescribeBroker(request, memory));
    }

    [Fact]
    public void DescribeBroker_IgnoresAnEndpointFoundSomewhereElse()
    {
        var request = new MqttEndpointRequest("broker.invalid", "user", null, MqttTransportMode.Auto);
        var elsewhere = new MqttEndpointMemory("other.invalid", "user", 8883, MqttTransport.Tcp, true);

        Assert.Equal("broker.invalid — not connected yet", MqttStatusText.DescribeBroker(request, elsewhere));
    }

    [Fact]
    public void DescribeLastCommand_FallsBackToTheEntityIdTheWireCarried()
    {
        var record = new MqttCommandRecord(Now.AddMinutes(-5), "quiet_mode");

        Assert.Equal("quiet_mode — 5 min ago", MqttStatusText.DescribeLastCommand(record, Now));
        Assert.Equal("Quiet mode — 5 min ago",
            MqttStatusText.DescribeLastCommand(record, Now, _ => "Quiet mode"));
    }

    [Fact]
    public void DescribeLastCommand_SaysSoWhenNothingHasArrived() =>
        Assert.Equal("Nothing received yet", MqttStatusText.DescribeLastCommand(null, Now));

    [Fact]
    public void Describe_NamesTheTransportInEveryVerdict()
    {
        foreach (var outcome in Enum.GetValues<MqttProbeOutcome>())
        {
            string sentence = MqttStatusText.Describe(new(outcome, "why"), MqttTransport.WebSocket);

            Assert.Contains("WebSocket", sentence);
        }
    }

    [Fact]
    public void Describe_OfACertificateFailurePointsAtNoControlThePanelDoesNotCarry()
    {
        string sentence = MqttStatusText.Describe(
            new(MqttProbeOutcome.TlsUntrusted, "the remote certificate is invalid"), MqttTransport.Tcp);

        // The reason the far end gave, and nothing sending a reader to look for a certificate-trust
        // field: the panel has none, and trust is configured by the host in code.
        Assert.Contains("the remote certificate is invalid", sentence);
        Assert.DoesNotContain("credentials", sentence);
        Assert.DoesNotContain("setting", sentence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_SendsARefusedCredentialToThePassword()
    {
        string sentence = MqttStatusText.Describe(
            new(MqttProbeOutcome.AuthRejected, "NotAuthorised"), MqttTransport.Tcp);

        Assert.Contains("rejected these credentials", sentence);
    }

    [Fact]
    public void Describe_OfAProgressLineTellsTheTwoStagesApart()
    {
        string port = MqttStatusText.Describe(
            new MqttSearchProgress(MqttSearchStage.Port, 1883, MqttTransport.Tcp));
        string transport = MqttStatusText.Describe(
            new MqttSearchProgress(MqttSearchStage.Transport, 1883, MqttTransport.Tcp));

        Assert.Equal("Trying TCP on port 1883…", port);
        Assert.NotEqual(port, transport);
        Assert.Contains("asking the broker", transport);
    }

    [Fact]
    public void Describe_OfAFinishedStageWithNoResultDoesNotReadAsASuccess()
    {
        string sentence = MqttStatusText.Describe(
            new MqttSearchProgress(MqttSearchStage.Finished, 1883, MqttTransport.Tcp));

        Assert.DoesNotContain("connected", sentence);
    }

    [Fact]
    public void Describe_OfAnEmptyRunSaysThereWasNothingToTry() =>
        Assert.Equal("No broker host set.", MqttStatusText.Describe(new MqttProbeReport([])));

    [Fact]
    public void Describe_OfASuccessfulRunIsTheOutcomeAndNotTheAttempts()
    {
        MqttProbeReport report = new([
            new(new(1883, MqttTransport.Tcp), MqttProbeOutcome.Unreachable),
            new(new(443, MqttTransport.WebSocket), MqttProbeOutcome.Success),
        ]);

        string sentence = MqttStatusText.Describe(report);

        // The question was whether these settings work. The answer is yes, over what and on which
        // port — a leg that failed on the way there is the search describing itself, and under a
        // success it reads as a fault to go and investigate.
        Assert.Equal("Connected over WebSocket on port 443.", sentence);
    }

    [Fact]
    public void Describe_OfAFailedRunListsWhatWasTriedWithoutRepeatingItself()
    {
        MqttProbeReport report = new([
            new(new(1883, MqttTransport.Tcp), MqttProbeOutcome.TimedOut),
            new(new(8883, MqttTransport.Tcp), MqttProbeOutcome.TimedOut),
            new(new(443, MqttTransport.WebSocket), MqttProbeOutcome.TimedOut),
        ]);

        string sentence = MqttStatusText.Describe(report);

        // Nothing answered anywhere, which is the one case where what was tried is the finding.
        Assert.Equal("The broker was not reached. TCP did not answer; WebSocket did not answer.",
            sentence);
    }

    [Fact]
    public void IsFailure_IsTrueForAnythingButASuccessfulRun()
    {
        Assert.False(MqttStatusText.IsFailure(
            new([new(new(1883, MqttTransport.Tcp), MqttProbeOutcome.Success)])));
        Assert.True(MqttStatusText.IsFailure(
            new([new(new(1883, MqttTransport.Tcp), MqttProbeOutcome.TlsUntrusted)])));
    }

    [Fact]
    public void Name_GivesEveryConnectionStateItsOwnWording()
    {
        var wordings = Enum.GetValues<MqttConnectionState>().Select(MqttStatusText.Name).ToList();

        Assert.All(wordings, w => Assert.False(string.IsNullOrWhiteSpace(w)));
        Assert.Equal(wordings.Count, wordings.Distinct(StringComparer.Ordinal).Count());
    }
}
