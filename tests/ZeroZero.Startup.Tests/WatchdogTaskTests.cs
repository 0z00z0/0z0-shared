using Microsoft.Win32.TaskScheduler;
using Xunit;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace ZeroZero.Startup.Tests;

/// <summary>The watchdog against the real scheduler, under disposable task names in the root
/// folder. Writing the definition needs an elevated process — the scheduler refuses a
/// highest-run-level task from a standard token — so those tests are skipped, and reported as
/// skipped, from one. The decision they drive is covered without a scheduler in
/// <see cref="WatchdogEnsureTests"/>.</summary>
public sealed class WatchdogTaskTests : IDisposable
{
    private static readonly string CommandInterpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>A prefix of its own, not the logon tests': theirs sweeps every task carrying it at
    /// first use, and the two classes run in parallel.</summary>
    private const string Prefix = "ZeroZero.Watchdog.Tests.";

    static WatchdogTaskTests() => Sweep();

    private readonly string _name = Prefix + Guid.NewGuid().ToString("N");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ZeroZero.Startup.Tests.Watchdog." + Guid.NewGuid().ToString("N"));
    private readonly RecordingLogSink _log = new();

    private WatchdogTask Watchdog(Func<string, bool>? registerWhen = null, string? logonTaskName = null) =>
        new(new WatchdogTaskOptions
        {
            TaskName = _name,
            LogonTaskName = logonTaskName,
            Description = "Disposable test watchdog. Delete freely.",
            ExecutablePath = CommandInterpreter,
            Arguments = "/c exit 0",
            StartCause = WatchdogStartCause.Person,
            HoldMarkerPath = Path.Combine(_folder, "watchdog-hold.marker"),
            RegisterWhen = registerWhen,
            Log = _log,
        });

    public void Dispose()
    {
        try
        {
            using var service = new TaskService();
            service.RootFolder.DeleteTask(_name, exceptionOnNotExists: false);
        }
        catch (UnauthorizedAccessException) { }

        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    /// <summary>Removes what a run that died left behind.</summary>
    private static void Sweep()
    {
        try
        {
            using var service = new TaskService();
            List<string> leftovers = service.RootFolder.Tasks
                .Where(task => task.Name.StartsWith(Prefix, StringComparison.Ordinal))
                .Select(task => task.Name)
                .ToList();
            foreach (string name in leftovers)
                service.RootFolder.DeleteTask(name, exceptionOnNotExists: false);
        }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A reading through a scheduler connection of its own, so what a test asserts was not
    /// answered by the object under test.</summary>
    private T ReadIndependently<T>(Func<ScheduledTask?, T> read)
    {
        using var service = new TaskService();
        using ScheduledTask? task = service.GetTask(_name);
        return read(task);
    }

    [ElevatedFact]
    public void AnAbsentWatchdogIsRegisteredWithAllThreeProbes()
    {
        using WatchdogTask watchdog = Watchdog();

        WatchdogEnsureResult result = watchdog.Ensure();

        Assert.Equal(WatchdogEnsureOutcome.Registered, result.Outcome);
        Assert.Equal(3, ReadIndependently(task => task!.Definition.Triggers.Count));
    }

    [ElevatedFact]
    public void ASecondStartLeavesTheRegisteredWatchdogAlone()
    {
        using WatchdogTask watchdog = Watchdog();
        watchdog.Ensure();

        WatchdogEnsureResult again = watchdog.Ensure();

        // The deviations first: a failure then names what the scheduler gave back differently from
        // what was written, rather than only saying the outcome was the wrong one.
        Assert.Empty(again.Deviations);
        Assert.Equal(WatchdogEnsureOutcome.AlreadyCorrect, again.Outcome);
    }

    [ElevatedFact]
    public void AWatchdogAnOlderBuildLeftBehindIsRewritten()
    {
        using WatchdogTask watchdog = Watchdog();
        watchdog.Ensure();

        using (var service = new TaskService())
        using (ScheduledTask task = service.GetTask(_name)!)
        {
            task.Definition.Settings.ExecutionTimeLimit = TimeSpan.FromHours(72);
            task.Definition.Settings.StopIfGoingOnBatteries = true;
            task.RegisterChanges();
        }

        WatchdogEnsureResult repaired = watchdog.Ensure();

        Assert.Equal(WatchdogEnsureOutcome.Registered, repaired.Outcome);
        Assert.NotEmpty(repaired.Deviations);
        Assert.Equal(TimeSpan.Zero, ReadIndependently(task => task!.Definition.Settings.ExecutionTimeLimit));
        Assert.False(ReadIndependently(task => task!.Definition.Settings.StopIfGoingOnBatteries));
    }

    [ElevatedFact]
    public void DeletingSaysWhetherThereWasATaskToDelete()
    {
        using WatchdogTask watchdog = Watchdog();
        watchdog.Ensure();

        Assert.True(watchdog.Delete());
        Assert.False(watchdog.Delete());
    }

    [Fact]
    public void AnExecutableTheApplicationRefusesToRegisterFromWritesNothing()
    {
        using WatchdogTask watchdog = Watchdog(registerWhen: _ => false);

        WatchdogEnsureResult result = watchdog.Ensure();

        Assert.Equal(WatchdogEnsureOutcome.Skipped, result.Outcome);
        Assert.Null(ReadIndependently(task => task?.Name));
    }

    [Fact]
    public void AnIdentityThatCannotBeReadIsAnOutcomeRatherThanAThrowOutOfStartUp()
    {
        using WatchdogTask watchdog = Watchdog();
        watchdog.Identity = () => throw new InvalidOperationException("no identity");

        WatchdogEnsureResult result = watchdog.Ensure();

        Assert.Equal(WatchdogEnsureOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void TheHoldIsTheOneTheOptionsName()
    {
        using WatchdogTask watchdog = Watchdog();

        watchdog.Hold.Hold();

        Assert.True(File.Exists(Path.Combine(_folder, "watchdog-hold.marker")));
        Assert.True(watchdog.Hold.IsHeld);
    }

    [Fact]
    public void TwoTasksOfOneNameAreRefusedAndNeitherIsWritten()
    {
        // Writing the watchdog over the logon task would replace it, and the person's choice about
        // starting at logon would go with it.
        using WatchdogTask watchdog = Watchdog(logonTaskName: _name);

        WatchdogEnsureResult result = watchdog.Ensure();

        Assert.Equal(WatchdogEnsureOutcome.Failed, result.Outcome);
        Assert.Contains(_name, result.Error!.Message, StringComparison.Ordinal);
        Assert.Null(ReadIndependently(task => task?.Name));
    }

    [Fact]
    public void ANameThatDiffersOnlyInCaseIsStillACollision()
    {
        // Scheduler names are case-insensitive, so the comparison is too.
        using WatchdogTask watchdog = Watchdog(logonTaskName: _name.ToUpperInvariant());

        Assert.Equal(WatchdogEnsureOutcome.Failed, watchdog.Ensure().Outcome);
        Assert.Null(ReadIndependently(task => task?.Name));
    }

    [Fact]
    public void ALogonTaskOfAnotherNameIsNoCollision()
    {
        using WatchdogTask watchdog = Watchdog(registerWhen: _ => false, logonTaskName: _name + ".Logon");

        Assert.Equal(WatchdogEnsureOutcome.Skipped, watchdog.Ensure().Outcome);
    }

    [Fact]
    public void ATaskNameIsRequired()
    {
        Assert.Throws<ArgumentException>(() => new WatchdogTask(new WatchdogTaskOptions
        {
            TaskName = " ",
            StartCause = WatchdogStartCause.Person,
            HoldMarkerPath = "x",
        }));
        Assert.Throws<ArgumentNullException>(() => new WatchdogTask(null!));
    }
}
