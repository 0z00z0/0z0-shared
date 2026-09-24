using Xunit;

namespace ZeroZero.Startup.Tests;

/// <summary>The decision behind keeping the watchdog correct, over delegates, with no scheduler
/// anywhere. What it does against a real task is in <see cref="WatchdogTaskTests"/>.</summary>
public class WatchdogEnsureTests
{
    private readonly RecordingLogSink _log = new();
    private int _deviationReads;
    private int _writes;
    private int _stops;

    private WatchdogEnsureResult Run(bool exists, string[] deviations, bool? mayRegister = null,
                                     Exception? writeFails = null, bool? withinBound = null) =>
        WatchdogEnsure.Run(
            mayRegister: mayRegister is null ? null : () => mayRegister.Value,
            withinRestartBound: withinBound is null ? null : () => withinBound.Value,
            exists: () => exists,
            deviations: () =>
            {
                _deviationReads++;
                return deviations;
            },
            write: () =>
            {
                _writes++;
                if (writeFails is not null) throw writeFails;
            },
            stopProbing: () => _stops++,
            _log);

    [Fact]
    public void AnAbsentTaskIsWritten()
    {
        // The opposite of the logon task: the watchdog is the application's own backstop, so an
        // absent one is created rather than read as a choice to go without.
        WatchdogEnsureResult result = Run(exists: false, []);

        Assert.Equal(WatchdogEnsureOutcome.Registered, result.Outcome);
        Assert.Equal([WatchdogEnsure.NotRegistered], result.Deviations);
        Assert.Equal(1, _writes);
        Assert.Equal(0, _deviationReads);
    }

    [Fact]
    public void ACorrectTaskIsNotRewritten()
    {
        WatchdogEnsureResult result = Run(exists: true, []);

        Assert.Equal(WatchdogEnsureOutcome.AlreadyCorrect, result.Outcome);
        Assert.Empty(result.Deviations);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void ADeviatingTaskIsWrittenOnceAndTheDeviationsAreReported()
    {
        WatchdogEnsureResult result = Run(exists: true, ["is disabled", "does not probe when the machine resumes"]);

        Assert.Equal(WatchdogEnsureOutcome.Registered, result.Outcome);
        Assert.Equal(["is disabled", "does not probe when the machine resumes"], result.Deviations);
        Assert.Equal(1, _writes);
        Assert.Contains(_log.Infos, line => line.Contains("is disabled", StringComparison.Ordinal)
                                         && line.Contains("does not probe when the machine resumes", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExecutableTheApplicationRefusesToRegisterFromIsLeftAlone()
    {
        // A development build: a task pointing at build output starts stale binaries for weeks.
        WatchdogEnsureResult result = Run(exists: false, [], mayRegister: false);

        Assert.Equal(WatchdogEnsureOutcome.Skipped, result.Outcome);
        Assert.Empty(result.Deviations);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void AnExecutableTheApplicationAcceptsIsRegistered()
    {
        WatchdogEnsureResult result = Run(exists: false, [], mayRegister: true);

        Assert.Equal(WatchdogEnsureOutcome.Registered, result.Outcome);
        Assert.Equal(1, _writes);
    }

    [Fact]
    public void TooManyRestartsStopTheProbingInsteadOfWritingTheTask()
    {
        // The guard the restart bound exists for: a task that would otherwise be rewritten, and
        // would then keep starting an application that cannot run, is disabled instead.
        WatchdogEnsureResult result = Run(exists: true, ["is disabled"], withinBound: false);

        Assert.Equal(WatchdogEnsureOutcome.Stopped, result.Outcome);
        Assert.Empty(result.Deviations);
        Assert.Equal(1, _stops);
        Assert.Equal(0, _writes);
        Assert.Equal(0, _deviationReads);
    }

    [Fact]
    public void AStartInsideTheBoundWritesTheTaskAsUsual()
    {
        WatchdogEnsureResult result = Run(exists: false, [], withinBound: true);

        Assert.Equal(WatchdogEnsureOutcome.Registered, result.Outcome);
        Assert.Equal(1, _writes);
        Assert.Equal(0, _stops);
    }

    [Fact]
    public void AWriteThatFailsIsAnOutcomeCarryingTheException()
    {
        var refusal = new UnauthorizedAccessException("refused");

        WatchdogEnsureResult result = Run(exists: true, ["does not run elevated"], writeFails: refusal);

        Assert.Equal(WatchdogEnsureOutcome.Failed, result.Outcome);
        Assert.Same(refusal, result.Error);
        Assert.Equal(["does not run elevated"], result.Deviations);
        Assert.Contains(_log.Errors, entry => ReferenceEquals(entry.Error, refusal));
    }

    [Fact]
    public void AReadThatFailsIsAnOutcomeCarryingTheException()
    {
        var outage = new InvalidOperationException("no scheduler");

        WatchdogEnsureResult result = WatchdogEnsure.Run(null, null, () => throw outage, () => [], () => _writes++, null, _log);

        Assert.Equal(WatchdogEnsureOutcome.Failed, result.Outcome);
        Assert.Same(outage, result.Error);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void ANullDelegateIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => WatchdogEnsure.Run(null, null, null!, () => [], () => { }));
        Assert.Throws<ArgumentNullException>(() => WatchdogEnsure.Run(null, null, () => true, null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => WatchdogEnsure.Run(null, null, () => true, () => [], null!));
        // A bound with no way to stop probing would reach the bound and do nothing about it.
        Assert.Throws<ArgumentNullException>(() => WatchdogEnsure.Run(null, () => true, () => true, () => [], () => { }));
    }
}
