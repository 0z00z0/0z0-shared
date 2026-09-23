using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;
using Xunit;

namespace ZeroZero.Startup.Tests;

/// <summary>The watchdog definition as the real scheduler library builds it, without registering
/// anything. The identity is invented: nothing here reaches the scheduler beyond a connection to
/// it.</summary>
public sealed class WatchdogTaskDefinitionTests : IDisposable
{
    private static readonly TaskIdentity Identity = new(@"MACHINE\someone", "S-1-5-21-1-2-3-1001");
    private static readonly string Executable = Path.Combine(Path.GetTempPath(), "ZeroZero.Startup.Tests", "app.exe");

    private readonly TaskService _service = new();

    public void Dispose() => _service.Dispose();

    private static WatchdogTaskOptions Options(string arguments = "--watchdog-relaunch") => new()
    {
        TaskName = "ZeroZero.Startup.Tests.Watchdog",
        Description = "described",
        Arguments = arguments,
        HoldMarkerPath = Path.Combine(Path.GetTempPath(), "ZeroZero.Startup.Tests", "hold.marker"),
    };

    private TaskDefinition Build(WatchdogTaskOptions? options = null) =>
        WatchdogTaskDefinition.Build(_service, options ?? Options(), Identity, Executable);

    [Fact]
    public void TheProbeRunsOnTheInterval_OnUnlock_AndOnResume()
    {
        using TaskDefinition definition = Build();

        TimeTrigger repeating = Assert.Single(definition.Triggers.OfType<TimeTrigger>());
        Assert.Equal(TimeSpan.FromMinutes(5), repeating.Repetition.Interval);

        SessionStateChangeTrigger unlock = Assert.Single(definition.Triggers.OfType<SessionStateChangeTrigger>());
        Assert.Equal(TaskSessionStateChangeType.SessionUnlock, unlock.StateChange);
        Assert.Equal(Identity.AccountName, unlock.UserId);
        Assert.Equal(TimeSpan.FromSeconds(5), unlock.Delay);

        EventTrigger resume = Assert.Single(definition.Triggers.OfType<EventTrigger>());
        Assert.Contains("Microsoft-Windows-Power-Troubleshooter", resume.Subscription, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(15), resume.Delay);
    }

    [Fact]
    public void TheStartBoundaryIsAlreadyPast_SoTheRepetitionSchedulesTheProbes()
    {
        using TaskDefinition definition = Build();

        TimeTrigger repeating = Assert.Single(definition.Triggers.OfType<TimeTrigger>());
        Assert.True(repeating.StartBoundary < DateTime.Now);
    }

    [Fact]
    public void TheActionStartsTheExecutableWithTheRelaunchArgument()
    {
        using TaskDefinition definition = Build();

        ExecAction action = Assert.IsType<ExecAction>(Assert.Single(definition.Actions));
        Assert.Equal(Executable, action.Path);
        Assert.Equal("--watchdog-relaunch", action.Arguments);
        Assert.Equal(Path.GetDirectoryName(Executable), action.WorkingDirectory);
    }

    [Fact]
    public void NoArgumentAtAllIsWrittenAsNoneRatherThanAnEmptyOne()
    {
        // An empty string round-trips through the scheduler as null, which the deviation read then
        // compares against "" — the two have to agree or every start rewrites the task.
        using TaskDefinition definition = Build(Options(arguments: ""));

        ExecAction action = Assert.IsType<ExecAction>(Assert.Single(definition.Actions));
        Assert.Null(action.Arguments);
        Assert.Empty(WatchdogTaskDefinition.Deviations(definition, Options(arguments: ""), Executable));
    }

    [Fact]
    public void TheSchedulerIsStoppedFromKillingWhatItStarted()
    {
        using TaskDefinition definition = Build();
        TaskSettings settings = definition.Settings;

        Assert.False(settings.DisallowStartIfOnBatteries);
        Assert.False(settings.StopIfGoingOnBatteries);
        Assert.False(settings.AllowHardTerminate);
        Assert.Equal(TimeSpan.Zero, settings.ExecutionTimeLimit);
        Assert.Equal(TaskInstancesPolicy.IgnoreNew, settings.MultipleInstances);
        Assert.False(settings.RunOnlyIfIdle);
        Assert.Equal(ProcessPriorityClass.Normal, settings.Priority);
        Assert.True(settings.StartWhenAvailable);
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void ItRunsElevatedOnTheInteractiveTokenAsTheGivenIdentity()
    {
        using TaskDefinition definition = Build();

        Assert.Equal(Identity.Sid, definition.Principal.UserId);
        Assert.Equal(TaskRunLevel.Highest, definition.Principal.RunLevel);
        Assert.Equal(TaskLogonType.InteractiveToken, definition.Principal.LogonType);
    }

    [Fact]
    public void ADefinitionTheComponentJustBuiltHasNothingToRepair()
    {
        using TaskDefinition definition = Build();

        Assert.Empty(WatchdogTaskDefinition.Deviations(definition, Options(), Executable));
    }

    [Fact]
    public void ADisabledWatchdogIsADeviation()
    {
        // Unlike the logon task, whose enabled flag is the person's choice: this one is the
        // application's own backstop and a disabled one is repaired.
        using TaskDefinition definition = Build();
        definition.Settings.Enabled = false;

        Assert.Contains("is disabled", WatchdogTaskDefinition.Deviations(definition, Options(), Executable));
    }

    [Fact]
    public void EachMissingTriggerIsItsOwnDeviation()
    {
        using TaskDefinition definition = Build();
        definition.Triggers.Clear();

        IReadOnlyList<string> found = WatchdogTaskDefinition.Deviations(definition, Options(), Executable);

        Assert.Contains("has no repeating probe", found);
        Assert.Contains("does not probe when the workstation is unlocked", found);
        Assert.Contains("does not probe when the machine resumes", found);
    }

    [Fact]
    public void AnIntervalOtherThanTheOneAskedForIsADeviation()
    {
        using TaskDefinition definition = Build();
        definition.Triggers.OfType<TimeTrigger>().Single().Repetition.Interval = TimeSpan.FromHours(1);

        Assert.Contains(WatchdogTaskDefinition.Deviations(definition, Options(), Executable),
                        line => line.StartsWith("probes every", StringComparison.Ordinal));
    }

    [Fact]
    public void ATaskPointingAtAnotherInstallationIsADeviation()
    {
        // The upgrade case: the task survives, the executable moves, and the old path would keep
        // starting binaries that are no longer there.
        using TaskDefinition definition = Build();
        string moved = Path.Combine(Path.GetTempPath(), "ZeroZero.Startup.Tests", "elsewhere", "app.exe");

        Assert.Contains(WatchdogTaskDefinition.Deviations(definition, Options(), moved),
                        line => line.StartsWith("starts '", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSettingsTheSchedulerDefaultsToAreEachTheirOwnDeviation()
    {
        using TaskDefinition definition = Build();
        TaskSettings settings = definition.Settings;
        settings.DisallowStartIfOnBatteries = true;
        settings.StopIfGoingOnBatteries = true;
        settings.AllowHardTerminate = true;
        settings.ExecutionTimeLimit = TimeSpan.FromHours(72);
        settings.StartWhenAvailable = false;

        IReadOnlyList<string> found = WatchdogTaskDefinition.Deviations(definition, Options(), Executable);

        Assert.Contains("starts only on mains power", found);
        Assert.Contains("stops when the machine goes on battery", found);
        Assert.Contains("may be hard-terminated", found);
        Assert.Contains("skips a probe missed while the machine was off", found);
        Assert.Contains(found, line => line.StartsWith("has an execution time limit", StringComparison.Ordinal));
    }
}
