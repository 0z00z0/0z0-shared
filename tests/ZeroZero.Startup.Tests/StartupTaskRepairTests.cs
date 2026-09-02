using Xunit;
using ZeroZero.Startup;

namespace ZeroZero.Startup.Tests;

/// <summary>The repair decision over delegates, with no scheduler anywhere. What the decision does
/// against a real task is in <see cref="StartupTaskTests"/>.</summary>
public class StartupTaskRepairTests
{
    private readonly RecordingLogSink _log = new();
    private int _deviationReads;
    private int _rewrites;
    private int _verifications;

    private StartupTaskRepairResult Run(bool exists, string[] deviations, bool? verified = null, Exception? rewriteFails = null, Exception? verifyFails = null) =>
        StartupTaskRepair.Run(
            exists: () => exists,
            deviations: () =>
            {
                _deviationReads++;
                return deviations;
            },
            rewrite: () =>
            {
                _rewrites++;
                if (rewriteFails is not null) throw rewriteFails;
            },
            verify: verified is null && verifyFails is null ? null : () =>
            {
                _verifications++;
                if (verifyFails is not null) throw verifyFails;
                return verified!.Value;
            },
            _log);

    [Fact]
    public void AnAbsentTaskIsLeftAbsent()
    {
        StartupTaskRepairResult result = Run(exists: false, ["anything"]);

        Assert.Equal(StartupTaskRepairOutcome.NotRegistered, result.Outcome);
        Assert.Empty(result.Deviations);
        Assert.Equal(0, _deviationReads);
        Assert.Equal(0, _rewrites);
    }

    [Fact]
    public void ACorrectTaskIsNotRewritten()
    {
        StartupTaskRepairResult result = Run(exists: true, [], verified: true);

        Assert.Equal(StartupTaskRepairOutcome.AlreadyCorrect, result.Outcome);
        Assert.Equal(0, _rewrites);
        Assert.Equal(0, _verifications);
    }

    [Fact]
    public void ADeviatingTaskIsRewrittenOnceAndTheDeviationsAreReported()
    {
        StartupTaskRepairResult result = Run(exists: true, ["starts only on mains power", "runs at BelowNormal priority"]);

        Assert.Equal(StartupTaskRepairOutcome.Repaired, result.Outcome);
        Assert.Equal(["starts only on mains power", "runs at BelowNormal priority"], result.Deviations);
        Assert.Null(result.Error);
        Assert.Equal(1, _rewrites);
        Assert.Contains(_log.Infos, line => line.Contains("starts only on mains power", StringComparison.Ordinal) && line.Contains("runs at BelowNormal priority", StringComparison.Ordinal));
    }

    [Fact]
    public void ARewriteThatFailsIsAnOutcomeCarryingTheException()
    {
        var refusal = new UnauthorizedAccessException("refused");

        StartupTaskRepairResult result = Run(exists: true, ["does not run elevated"], rewriteFails: refusal);

        Assert.Equal(StartupTaskRepairOutcome.RepairFailed, result.Outcome);
        Assert.Same(refusal, result.Error);
        Assert.Equal(["does not run elevated"], result.Deviations);
        Assert.Contains(_log.Errors, entry => ReferenceEquals(entry.Error, refusal));
    }

    [Fact]
    public void AReadThatFailsIsAnOutcomeCarryingTheException()
    {
        var outage = new InvalidOperationException("no scheduler");

        StartupTaskRepairResult result = StartupTaskRepair.Run(() => throw outage, () => [], () => _rewrites++, null, _log);

        Assert.Equal(StartupTaskRepairOutcome.RepairFailed, result.Outcome);
        Assert.Same(outage, result.Error);
        Assert.Equal(0, _rewrites);
    }

    [Fact]
    public void ARewrittenTaskThatDoesNotRunIsNotReportedRepaired()
    {
        StartupTaskRepairResult result = Run(exists: true, ["starts only on mains power"], verified: false);

        Assert.Equal(StartupTaskRepairOutcome.VerificationFailed, result.Outcome);
        Assert.Equal(1, _rewrites);
        Assert.Equal(1, _verifications);
        Assert.NotEmpty(_log.Errors);
    }

    [Fact]
    public void ARewrittenTaskThatRunsIsRepaired()
    {
        StartupTaskRepairResult result = Run(exists: true, ["starts only on mains power"], verified: true);

        Assert.Equal(StartupTaskRepairOutcome.Repaired, result.Outcome);
        Assert.Equal(1, _verifications);
    }

    [Fact]
    public void AVerificationThatThrowsIsAFailedVerificationCarryingTheException()
    {
        var refusal = new InvalidOperationException("disabled");

        StartupTaskRepairResult result = Run(exists: true, ["starts only on mains power"], verifyFails: refusal);

        Assert.Equal(StartupTaskRepairOutcome.VerificationFailed, result.Outcome);
        Assert.Same(refusal, result.Error);
    }

    [Fact]
    public void ANullDelegateIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => StartupTaskRepair.Run(null!, () => [], () => { }, null));
        Assert.Throws<ArgumentNullException>(() => StartupTaskRepair.Run(() => true, null!, () => { }, null));
        Assert.Throws<ArgumentNullException>(() => StartupTaskRepair.Run(() => true, () => [], null!, null));
    }
}
