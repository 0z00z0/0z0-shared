using Microsoft.Win32.TaskScheduler;
using Xunit;
using ZeroZero.Startup;

namespace ZeroZero.Startup.Tests;

/// <summary>Against the real scheduler, under disposable names. Every reading a test relies on is
/// taken through a scheduler connection of its own, never through the object under test. Tests
/// that need a highest-run-level task are skipped from a standard token and say so.</summary>
public class StartupTaskTests
{
    private static readonly TimeSpan RunWait = TimeSpan.FromSeconds(60);

    /// <summary>Enough for the scheduler to have started the program — measured at 36 to 409 ms —
    /// and short enough that a test which only needs the wait to end while the program is up does
    /// not pay the whole verification wait.</summary>
    private static readonly TimeSpan ResidentWait = TimeSpan.FromSeconds(3);

    static StartupTaskTests() => DisposableTask.Sweep();

    [Fact]
    public void ATaskThatIsNotRegisteredReadsAsAbsentAndRefusesEveryChange()
    {
        using var disposable = new DisposableTask();

        Assert.False(disposable.Task.IsEnabled);
        Assert.Equal(StartupTaskState.Absent, disposable.Task.Read());
        Assert.False(disposable.Task.Delete());
        Assert.Throws<InvalidOperationException>(() => disposable.Task.Enable());
        Assert.Throws<InvalidOperationException>(() => disposable.Task.Disable());
        Assert.Throws<InvalidOperationException>(() => disposable.Task.DemandStart(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ARegisteredTaskReadsAsEnabledAndNeverRun()
    {
        using var disposable = new DisposableTask();
        disposable.Register();

        StartupTaskState state = disposable.Task.Read();

        Assert.True(state.Exists);
        Assert.True(state.Enabled);
        Assert.False(state.HasEverRun);
        Assert.Null(state.LastRun);
        Assert.Equal(StartupTask.NeverRunResult, state.LastResult);
        Assert.True(disposable.Task.IsEnabled);
    }

    [Fact]
    public void DisableAndEnableWriteThroughToTheScheduler()
    {
        using var disposable = new DisposableTask();
        disposable.Register();

        disposable.Task.Disable();
        Assert.False(disposable.Task.IsEnabled);
        Assert.False(disposable.ReadIndependently(task => task!.Enabled));

        disposable.Task.Enable();
        Assert.True(disposable.Task.IsEnabled);
        Assert.True(disposable.ReadIndependently(task => task!.Enabled));
    }

    [Fact]
    public void ADemandStartProvesTheTaskStartedItsExecutable()
    {
        using var disposable = new DisposableTask(exitCode: 0);
        disposable.Register();

        StartupTaskRunResult result = disposable.Task.DemandStart(RunWait);

        Assert.True(result.Ran, "The run did not end within the wait.");
        Assert.Equal(0, result.LastResult);
        Assert.True(result.Succeeded);
        StartupTaskState state = disposable.Task.Read();
        Assert.True(state.HasEverRun);
        Assert.NotNull(state.LastRun);
        Assert.NotEqual(StartupTask.NeverRunResult, disposable.ReadIndependently(task => task!.LastTaskResult));
    }

    [Fact]
    public void ADemandStartReportsTheExecutablesExitCode()
    {
        using var disposable = new DisposableTask(exitCode: 7);
        disposable.Register();

        StartupTaskRunResult result = disposable.Task.DemandStart(RunWait);

        Assert.True(result.Ran, "The run did not end within the wait.");
        Assert.Equal(7, result.LastResult);
        Assert.False(result.Succeeded);
        Assert.Equal(7, disposable.ReadIndependently(task => task!.LastTaskResult));
    }

    /// <summary>A program that stays resident never exits, so nothing settles inside the wait and
    /// the reading taken at the end of it is the whole answer.</summary>
    [Fact]
    public void ADemandStartCountsAProgramThatStaysResidentAsAStart()
    {
        using var disposable = new DisposableTask(arguments: DisposableTask.ResidentArguments);
        disposable.Register();

        StartupTaskRunResult result = disposable.Task.DemandStart(ResidentWait);

        Assert.False(result.Ran, "A resident program's run does not end, so nothing settles inside the wait.");
        Assert.True(result.StillRunning);
        Assert.True(result.Succeeded);
        Assert.Equal(StartupTask.RunningResult, result.LastResult);
        Assert.NotNull(result.LastRun);
        Assert.Equal(TaskState.Running, disposable.ReadIndependently(task => task!.State));
    }

    /// <summary>The multiple-instance policy is ignore-new, so a demand start of a task whose
    /// program is already up starts nothing and the scheduler records the refusal. That is a task
    /// which can run, and it is what a resident application's repair of its own logon task meets.
    /// </summary>
    [Fact]
    public void ADemandStartCountsARefusedStartAsAStartWhenAnInstanceIsAlreadyRunning()
    {
        using var disposable = new DisposableTask(arguments: DisposableTask.ResidentArguments);
        disposable.Register();
        disposable.Task.DemandStart(ResidentWait);

        StartupTaskRunResult result = disposable.Task.DemandStart(ResidentWait);

        Assert.True(result.StillRunning);
        Assert.True(result.Succeeded);
        Assert.Equal(StartupTask.AlreadyRunningResult, result.LastResult);
        Assert.Equal(StartupTask.AlreadyRunningResult, disposable.ReadIndependently(task => task!.LastTaskResult));
    }

    /// <summary>A task that cannot start what it points at never reaches the running state: the
    /// scheduler returns it to ready carrying the error, in under a tenth of a second. Accepting a
    /// running task at the end of the wait therefore does not let a task that starts nothing
    /// through.</summary>
    [Fact]
    public void ADemandStartOfATaskWhoseExecutableIsMissingIsARunThatHappenedAndFailed()
    {
        using var disposable = new DisposableTask(executablePath: DisposableTask.MissingExecutable, arguments: "");
        disposable.Register();

        StartupTaskRunResult result = disposable.Task.DemandStart(RunWait);

        Assert.True(result.Ran, "The scheduler reports a missing executable as a run that ended.");
        Assert.False(result.StillRunning);
        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.LastResult);
    }

    /// <summary>The repair verifies itself through the demand start, so a stale success code there
    /// makes a task whose executable failed verify as repaired. The elevated sibling drives this
    /// through <see cref="StartupTask.Repair"/> itself; a standard token cannot register the
    /// highest-run-level task a rewrite writes, so here the same composition is built by hand over
    /// a real task whose executable fails.</summary>
    [Fact]
    public void TheVerificationARepairRunsRefusesATaskWhoseExecutableFails()
    {
        using var disposable = new DisposableTask(exitCode: 7);
        disposable.Register();

        StartupTaskRepairResult result = StartupTaskRepair.Run(
            exists: () => true,
            deviations: () => ["something differs, so the repair reaches its verification"],
            rewrite: () => { },
            verify: () => disposable.Task.DemandStart(RunWait).Succeeded,
            disposable.Log);

        Assert.Equal(StartupTaskRepairOutcome.VerificationFailed, result.Outcome);
    }

    /// <summary>The same composition over a task whose program stays resident, which is what both
    /// consuming applications are. Before the deadline reading this was the case that always
    /// reported a failed verification.</summary>
    [Fact]
    public void TheVerificationARepairRunsAcceptsATaskWhoseProgramStaysResident()
    {
        using var disposable = new DisposableTask(arguments: DisposableTask.ResidentArguments);
        disposable.Register();

        StartupTaskRepairResult result = StartupTaskRepair.Run(
            exists: () => true,
            deviations: () => ["something differs, so the repair reaches its verification"],
            rewrite: () => { },
            verify: () => disposable.Task.DemandStart(ResidentWait).Succeeded,
            disposable.Log);

        Assert.Equal(StartupTaskRepairOutcome.Repaired, result.Outcome);
    }

    [Fact]
    public void DeleteRemovesTheTaskAndSaysWhetherThereWasOne()
    {
        using var disposable = new DisposableTask();
        disposable.Register();

        Assert.True(disposable.Task.Delete());

        Assert.Null(disposable.ReadIndependently(task => task));
        Assert.False(disposable.Task.Delete());
    }

    [Fact]
    public void RepairNeverCreatesATask()
    {
        using var disposable = new DisposableTask();

        StartupTaskRepairResult result = disposable.Task.Repair();

        Assert.Equal(StartupTaskRepairOutcome.NotRegistered, result.Outcome);
        Assert.Null(disposable.ReadIndependently(task => task));
    }

    [Fact]
    public void RepairLogsTheTasksStateAfterwards()
    {
        using var disposable = new DisposableTask();
        disposable.Register();

        disposable.Task.Repair();

        Assert.Contains(disposable.Log.Infos, line => line.Contains("registered, enabled, never run", StringComparison.Ordinal));
    }

    /// <summary>
    /// The repair promises never to throw, and the current identity is the one read that is not a
    /// scheduler call: read outside the delegates it would take the application down at start-up
    /// over a task it never needed to be running.
    /// </summary>
    [Fact]
    public void ARepairThatCannotReadTheCurrentIdentityIsAnOutcomeNotAThrow()
    {
        using var disposable = new DisposableTask();
        disposable.Register();
        disposable.Tamper(definition => definition.Settings.DisallowStartIfOnBatteries = true);
        disposable.Task.Identity = () => throw new InvalidOperationException("the identity cannot be read");

        StartupTaskRepairResult? result = null;
        Exception? thrown = Record.Exception(() => result = disposable.Task.Repair());

        Assert.True(thrown is null, $"Repair threw {thrown?.GetType().Name}; it promises the outcome instead.");
        Assert.Equal(StartupTaskRepairOutcome.RepairFailed, result!.Outcome);
        Assert.IsType<InvalidOperationException>(result.Error);
    }

    [UnelevatedFact]
    public void AStandardTokenIsRefusedTheHighestRunLevel()
    {
        using var disposable = new DisposableTask();

        Assert.Throws<UnauthorizedAccessException>(() => disposable.Task.Register());
        Assert.Null(disposable.ReadIndependently(task => task));
    }

    [UnelevatedFact]
    public void ARepairAStandardTokenCannotMakeIsAnOutcomeNotAThrow()
    {
        using var disposable = new DisposableTask();
        disposable.Register();

        StartupTaskRepairResult result = disposable.Task.Repair();

        Assert.Equal(StartupTaskRepairOutcome.RepairFailed, result.Outcome);
        Assert.Contains("does not run elevated", result.Deviations);
        Assert.IsType<UnauthorizedAccessException>(result.Error);
        Assert.True(disposable.ReadIndependently(task => task!.Enabled));
    }

    [ElevatedFact]
    public void RegisterWritesThePowerSafeElevatedLogonTask()
    {
        using var disposable = new DisposableTask();
        TaskIdentity identity = TaskIdentity.Current();

        disposable.Task.Register();

        disposable.ReadIndependently(task =>
        {
            Assert.NotNull(task);
            TaskDefinition definition = task.Definition;
            Assert.Equal(TaskRunLevel.Highest, definition.Principal.RunLevel);
            Assert.Equal(TaskLogonType.InteractiveToken, definition.Principal.LogonType);
            LogonTrigger trigger = Assert.IsType<LogonTrigger>(Assert.Single(definition.Triggers));
            Assert.Equal(identity.AccountName, trigger.UserId, ignoreCase: true);
            ExecAction action = Assert.IsType<ExecAction>(Assert.Single(definition.Actions));
            Assert.Equal(DisposableTask.CommandInterpreter, action.Path, ignoreCase: true);
            Assert.False(definition.Settings.DisallowStartIfOnBatteries);
            Assert.False(definition.Settings.StopIfGoingOnBatteries);
            Assert.False(definition.Settings.AllowHardTerminate);
            Assert.Equal(TimeSpan.Zero, definition.Settings.ExecutionTimeLimit);
            Assert.True(task.Enabled);
            return 0;
        });
        Assert.Empty(StartupTaskDefinition.Deviations(disposable.ReadIndependently(task => task!.Definition), DisposableTask.CommandInterpreter, disposable.Options.Arguments));
    }

    [ElevatedFact]
    public void RepairRewritesAnOlderBuildsSettingsAndKeepsTheUsersChoice()
    {
        using var disposable = new DisposableTask();
        disposable.Task.Register();
        disposable.Task.Disable();
        disposable.Tamper(definition =>
        {
            definition.Settings.DisallowStartIfOnBatteries = true;
            definition.Settings.ExecutionTimeLimit = TimeSpan.FromHours(72);
        });

        StartupTaskRepairResult first = disposable.Task.Repair();
        StartupTaskRepairResult second = disposable.Task.Repair();

        Assert.Equal(StartupTaskRepairOutcome.Repaired, first.Outcome);
        Assert.Contains(first.Deviations, deviation => deviation.Contains("mains power", StringComparison.Ordinal));
        Assert.Contains(first.Deviations, deviation => deviation.Contains("execution time limit", StringComparison.Ordinal));
        Assert.Equal(StartupTaskRepairOutcome.AlreadyCorrect, second.Outcome);
        Assert.False(disposable.ReadIndependently(task => task!.Enabled));
        Assert.False(disposable.ReadIndependently(task => task!.Definition.Settings.DisallowStartIfOnBatteries));
        Assert.Equal(TimeSpan.Zero, disposable.ReadIndependently(task => task!.Definition.Settings.ExecutionTimeLimit));
    }

    [ElevatedFact]
    public void RepairWithVerificationDemandStartsTheRewrittenTask()
    {
        using var disposable = new DisposableTask(exitCode: 0, verify: true);
        disposable.Task.Register();
        disposable.Tamper(definition => definition.Settings.DisallowStartIfOnBatteries = true);

        StartupTaskRepairResult result = disposable.Task.Repair();

        Assert.Equal(StartupTaskRepairOutcome.Repaired, result.Outcome);
        Assert.True(disposable.Task.Read().HasEverRun);
    }

    [ElevatedFact]
    public void RepairWithVerificationReportsATaskWhoseRunFails()
    {
        using var disposable = new DisposableTask(exitCode: 7, verify: true);
        disposable.Task.Register();
        disposable.Tamper(definition => definition.Settings.DisallowStartIfOnBatteries = true);

        StartupTaskRepairResult result = disposable.Task.Repair();

        Assert.Equal(StartupTaskRepairOutcome.VerificationFailed, result.Outcome);
        Assert.Equal(7, disposable.ReadIndependently(task => task!.LastTaskResult));
    }
}
