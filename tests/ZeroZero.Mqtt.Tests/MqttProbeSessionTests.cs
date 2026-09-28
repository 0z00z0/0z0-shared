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

    [Trait(Guard.Category, Guard.Value)]
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

    // ------------------------------------------------------------------------------------------
    // Apply repeats no test that has already passed on the same values.
    // ------------------------------------------------------------------------------------------

    private static readonly MqttProbeTarget Tested = new(
        Host: "broker.example.com", Port: 8883, Username: "desk", Password: "secret-one",
        ClientId: "desk01_probe", Transport: MqttTransportMode.Tcp, Encryption: MqttEncryptionMode.On,
        Memory: new MqttEndpointMemory("broker.example.com", "desk", 1883, MqttTransport.Tcp, false),
        CertificateTrust: MqttCertificateTrust.ForThumbprint("AB12"));

    private static MqttProbeSession PassedOn(MqttProbeTarget target)
    {
        var session = new MqttProbeSession();
        long token = session.Start(target);
        session.Settle(token, Succeeded(8883, MqttTransport.Tcp));
        session.Finish(token);
        return session;
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void ApplyAfterAPassOnTheSameValuesShowsThatPassAndStartsNoRun()
    {
        var session = PassedOn(Tested);
        // The line goes whenever a field moves, even when it moves back to what was tested.
        session.Clear();

        // The live connection's reconnect can move the remembered endpoint between the test and
        // Apply; that orders a sweep and is not one of the values being asked about.
        var applied = Tested with
        {
            Memory = new MqttEndpointMemory("broker.example.com", "desk", 8883, MqttTransport.Tcp, true),
        };

        Assert.False(session.TryReuse(MqttProbeTrigger.TestConnection, applied),
            "Test connection reused an earlier pass instead of testing");
        Assert.False(session.HasLine);

        Assert.True(session.TryReuse(MqttProbeTrigger.Apply, applied));
        Assert.StartsWith("Connected over TCP", session.Line, StringComparison.Ordinal);
        Assert.False(session.IsFailure);
        Assert.False(session.Busy);
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void ApplyTestsAgainWhenAnyOneValueDiffersOrTheNewestVerdictFailed()
    {
        var variants = new (string Field, MqttProbeTarget Target)[]
        {
            ("host", Tested with { Host = "other.example.com" }),
            ("port", Tested with { Port = 1883 }),
            ("automatic port", Tested with { Port = null }),
            ("transport", Tested with { Transport = MqttTransportMode.WebSocket }),
            ("encryption", Tested with { Encryption = MqttEncryptionMode.Off }),
            ("username", Tested with { Username = "desk2" }),
            ("password", Tested with { Password = "secret-two" }),
            ("certificate trust mode", Tested with { CertificateTrust = MqttCertificateTrust.SystemTrust }),
            ("pinned certificate", Tested with { CertificateTrust = MqttCertificateTrust.ForThumbprint("CD34") }),
            ("client id", Tested with { ClientId = "desk02_probe" }),
        };

        foreach (var (field, target) in variants)
        {
            var session = PassedOn(Tested);
            Assert.False(session.TryReuse(MqttProbeTrigger.Apply, target),
                $"a different {field} reused the pass on the earlier values");
        }

        // The newest verdict on the same values is a failure, so the pass before it answers nothing.
        var failedSince = PassedOn(Tested);
        long token = failedSince.Start(Tested);
        failedSince.Settle(token, new MqttProbeReport(
            [new MqttEndpointAttempt(new(8883, MqttTransport.Tcp), MqttProbeOutcome.Unreachable)]));
        failedSince.Finish(token);

        Assert.False(failedSince.TryReuse(MqttProbeTrigger.Apply, Tested),
            "an older pass answered for values whose newest test failed");
        Assert.True(failedSince.IsFailure);
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
