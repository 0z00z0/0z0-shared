using Microsoft.Win32.TaskScheduler;
using Xunit;
using ZeroZero.Startup;

namespace ZeroZero.Startup.Tests;

/// <summary>What a demand start decides, with no scheduler anywhere: the reading taken once the
/// wait is over, and what the result of one means. The same decisions against real tasks are in
/// <see cref="StartupTaskTests"/>, where the queued state cannot be produced on demand.</summary>
public class DemandStartDecisionTests
{
    private static readonly DateTime Before = new(2026, 01, 01, 12, 00, 00, DateTimeKind.Utc);
    private static readonly DateTime Moved = Before.AddSeconds(1);

    [Fact]
    public void ARunningTaskWhoseRunTimeMovedStartedItsProgram() =>
        Assert.True(StartupTask.StartedAndStillRunning(TaskState.Running, Moved, Before));

    /// <summary>The defect 0.7.2 closed: a run that is only queued has moved the run time on
    /// without starting anything, and must not count as a start.</summary>
    [Fact]
    public void AQueuedRunHasNotStartedItsProgram() =>
        Assert.False(StartupTask.StartedAndStillRunning(TaskState.Queued, Moved, Before));

    [Fact]
    public void ATaskBackAtReadyIsNotStillRunning() =>
        Assert.False(StartupTask.StartedAndStillRunning(TaskState.Ready, Moved, Before));

    /// <summary>A run time that never moved says the scheduler recorded no attempt at all, whatever
    /// state the task is in.</summary>
    [Fact]
    public void ARunTimeThatNeverMovedIsNotAStart() =>
        Assert.False(StartupTask.StartedAndStillRunning(TaskState.Running, Before, Before));

    [Fact]
    public void AProgramStillRunningWhenTheWaitEndedSucceeded() =>
        Assert.True(new StartupTaskRunResult(false, Moved, StartupTask.RunningResult, true).Succeeded);

    [Fact]
    public void AStartRefusedBecauseAnInstanceIsAliveSucceeded() =>
        Assert.True(new StartupTaskRunResult(false, Moved, StartupTask.AlreadyRunningResult, true).Succeeded);

    [Fact]
    public void ARunThatEndedWithZeroSucceeded() =>
        Assert.True(new StartupTaskRunResult(true, Moved, 0, false).Succeeded);

    [Fact]
    public void ARunThatEndedWithACodeOfItsOwnDidNotSucceed() =>
        Assert.False(new StartupTaskRunResult(true, Moved, 7, false).Succeeded);

    [Fact]
    public void ARunThatNeitherEndedNorIsRunningDidNotSucceed() =>
        Assert.False(new StartupTaskRunResult(false, null, null, false).Succeeded);

    /// <summary>Zero means an exit code only on a run that ended. Carried by anything else it is
    /// the code the scheduler writes on its way between states, and settles nothing.</summary>
    [Fact]
    public void AZeroOnAResultThatDidNotRunDidNotSucceed() =>
        Assert.False(new StartupTaskRunResult(false, null, 0, false).Succeeded);

    /// <summary>The wait has to outlast the scheduler's own reporting. A finished run keeps reading
    /// as running for seconds after the program is gone — measured 4.0 to 8.1 s under no added load —
    /// and a run still reading as running when the wait ends counts as a start, so a wait shorter
    /// than that reports a program which started and failed as one that started and stayed up.
    /// </summary>
    [Fact]
    public void TheVerificationWaitOutlastsTheSchedulersReportingOfAFinishedRun() =>
        Assert.True(StartupTask.VerificationWait >= TimeSpan.FromSeconds(10),
                    $"The verification wait is {StartupTask.VerificationWait.TotalSeconds:0} s, inside the window in which the scheduler still reports a finished run as running.");
}
