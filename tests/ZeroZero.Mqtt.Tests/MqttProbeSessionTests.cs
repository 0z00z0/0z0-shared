using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The line under the Test connection button. The ordering rules here are what stop a panel
/// ending stuck on "trying…" with the connection live, and what stop a cancelled probe leaving the
/// button disabled and the spinner turning for ever.</summary>
public class MqttProbeSessionTests
{
    private static MqttSearchProgress Trying(int port, MqttTransport transport) =>
        new(MqttSearchStage.Port, port, transport);

    private static MqttProbeReport Succeeded(int port, MqttTransport transport) =>
        new([new MqttEndpointAttempt(new(port, transport), MqttProbeOutcome.Success)]);

    // ------------------------------------------------------------------------------------------
    // The sweep is visible, and its verdict is final.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void EveryInvocationSaysSomethingImmediately()
    {
        var session = new MqttProbeSession();

        session.Start();

        Assert.True(session.HasLine);
        Assert.Equal("Testing…", session.Line);
        Assert.True(session.Busy);
    }

    [Fact]
    public void TheLineChangesOncePerCandidateRatherThanSettling()
    {
        // The churn is the point: several seconds of probing have no other visible evidence.
        var session = new MqttProbeSession();
        long token = session.Start();
        var sweep = MqttEndpointPlan.Sweep(
            new MqttEndpointRequest("broker.invalid", "user", null, MqttTransportMode.Auto), null);
        var seen = new List<string>();

        foreach (var candidate in sweep)
        {
            session.Report(token, Trying(candidate.Port, candidate.Transport));
            Assert.Contains(candidate.Port.ToString(), session.Line, StringComparison.Ordinal);
            seen.Add(session.Line);
        }

        // One line per endpoint the sweep offers — the encrypted and plain halves of one endpoint
        // are the same port and transport, so they share a line and nothing else does.
        int endpoints = sweep.Select(c => (c.Port, c.Transport)).Distinct().Count();
        Assert.True(endpoints > 4);
        Assert.Equal(endpoints, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EachLineNamesWhatIsBeingTriedRatherThanACandidateIndex()
    {
        var session = new MqttProbeSession();
        long token = session.Start();

        session.Report(token, Trying(9001, MqttTransport.WebSocket));

        Assert.Contains("WebSocket", session.Line);
        Assert.Contains("9001", session.Line);
        Assert.DoesNotContain("candidate", session.Line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFullSweepEndsOnItsVerdict()
    {
        var session = new MqttProbeSession();
        long token = session.Start();
        session.Report(token, Trying(1883, MqttTransport.Tcp));
        session.Report(token, Trying(8883, MqttTransport.Tcp));

        session.Settle(token, Succeeded(443, MqttTransport.WebSocket));
        session.Finish(token);

        Assert.StartsWith("Connected over WebSocket", session.Line, StringComparison.Ordinal);
        Assert.False(session.IsFailure);
        Assert.False(session.Busy);
    }

    [Fact]
    public void ALateProgressReportDoesNotOverwriteTheVerdict()
    {
        // The race that shows as a panel stuck on "trying…" with the connection already live.
        var session = new MqttProbeSession();
        long token = session.Start();
        session.Settle(token, Succeeded(443, MqttTransport.WebSocket));

        session.Report(token, Trying(80, MqttTransport.WebSocket));

        Assert.StartsWith("Connected over WebSocket", session.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedRunIsMarkedAsOne()
    {
        var session = new MqttProbeSession();
        long token = session.Start();

        session.Settle(token, new MqttProbeReport(
            [new MqttEndpointAttempt(new(1883, MqttTransport.Tcp), MqttProbeOutcome.AuthRejected)]));

        Assert.True(session.IsFailure);
        Assert.Contains("rejected these credentials", session.Line);
    }

    // ------------------------------------------------------------------------------------------
    // Supersession.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ASupersededRunCannotWriteOverItsSuccessor()
    {
        var session = new MqttProbeSession();
        long first = session.Start();
        long second = session.Start();
        session.Report(second, Trying(1883, MqttTransport.Tcp));
        string current = session.Line;

        session.Report(first, Trying(80, MqttTransport.WebSocket));
        session.Settle(first, Succeeded(80, MqttTransport.WebSocket));

        Assert.Equal(current, session.Line);
    }

    [Fact]
    public void TheSuccessorStillReportsAfterItsPredecessorFinishes()
    {
        var session = new MqttProbeSession();
        long first = session.Start();
        long second = session.Start();

        session.Finish(first);
        session.Settle(second, Succeeded(1883, MqttTransport.Tcp));

        Assert.StartsWith("Connected over TCP", session.Line, StringComparison.Ordinal);
        // The successor has not finished, so the controls stay held.
        Assert.True(session.Busy);
    }

    // ------------------------------------------------------------------------------------------
    // Nothing can strand the button or the spinner.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void FinishingReleasesTheControlsWhateverBecameOfTheRun()
    {
        var session = new MqttProbeSession();
        long token = session.Start();

        // No verdict, no progress — the cancelled-with-no-successor path.
        session.Finish(token);

        Assert.False(session.Busy);
    }

    [Fact]
    public void FinishingTwiceCannotTakeTheCountBelowZero()
    {
        var session = new MqttProbeSession();
        long first = session.Start();
        session.Finish(first);
        long second = session.Start();

        session.Finish(first);

        Assert.True(session.Busy);
    }

    [Fact]
    public void AbandoningReleasesTheControlsAtOnceRatherThanAtTheEndOfTheBudget()
    {
        var session = new MqttProbeSession();
        long token = session.Start();

        session.Abandon();

        Assert.False(session.Busy);
        // And the abandoned run's own completion changes nothing on the way out.
        session.Settle(token, Succeeded(1883, MqttTransport.Tcp));
        session.Finish(token);
        Assert.False(session.Busy);
        Assert.DoesNotContain("Connected", session.Line);
    }

    // ------------------------------------------------------------------------------------------
    // A request that never reached the network still answers.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ARefusedRequestReportsRatherThanAppearingToDoNothing()
    {
        var session = new MqttProbeSession();

        session.Refuse(MqttStrings.Default.Get("ReportNoHost"));

        Assert.Equal("No broker host set.", session.Line);
        Assert.False(session.Busy);
    }

    [Fact]
    public void ARefusalSurvivesALateReportFromWhateverRanBefore()
    {
        var session = new MqttProbeSession();
        long token = session.Start();
        session.Refuse("No broker host set.");

        session.Report(token, Trying(1883, MqttTransport.Tcp));

        Assert.Equal("No broker host set.", session.Line);
    }

    [Fact]
    public void ClearingRemovesTheLineWithoutClaimingAnything()
    {
        var session = new MqttProbeSession();
        long token = session.Start();
        session.Settle(token, Succeeded(1883, MqttTransport.Tcp));

        session.Clear();

        Assert.False(session.HasLine);
        Assert.False(session.IsFailure);
    }

    [Fact]
    public void ATranslatedSessionComposesItsLinesFromItsOwnStrings()
    {
        var session = new MqttProbeSession(new MqttPanelText(new MqttStrings(new Fixed(new()
        {
            ["TestRunning"] = "Tester…",
        }))));

        session.Start();

        Assert.Equal("Tester…", session.Line);
    }

    private sealed class Fixed(Dictionary<string, string> entries) : IMqttStringSource
    {
        public string? Find(string key) => entries.GetValueOrDefault(key);
    }
}
