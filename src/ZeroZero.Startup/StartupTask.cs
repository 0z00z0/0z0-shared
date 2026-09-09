using System.Globalization;
using Microsoft.Win32.TaskScheduler;
using ZeroZero.Primitives;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace ZeroZero.Startup;

/// <summary>The application's logon task, by name, in the scheduler's root folder. Registration
/// through the installer and the choice to have one at all stay with the application and its user;
/// this reads, enables, disables, deletes, repairs and demand-starts the task that is there.</summary>
public sealed class StartupTask : IDisposable
{
    /// <summary>The result code the scheduler reports for a task that has never run.</summary>
    public const int NeverRunResult = 0x00041303;

    /// <summary>The result code the scheduler reports while a run is in flight.</summary>
    public const int RunningResult = 0x00041301;

    /// <summary>The result code the scheduler reports when it refuses to start a task because an
    /// instance of it is already running, which is what the ignore-new multiple-instance policy of
    /// this definition asks for. A demand start of a resident application's own logon task reports
    /// this rather than starting a second copy.</summary>
    public const int AlreadyRunningResult = unchecked((int)0x800710E0);

    /// <summary>How long a repair's verification waits before it decides. The whole wait is spent
    /// where the started program stays resident, because such a run never ends, so it is paid on
    /// the start-up path of the application being repaired. It is not free to shorten: the
    /// scheduler holds a finished run in the running state for seconds after the program is gone —
    /// measured 4.0 to 8.1 s on an idle machine and a median of 6.1 s with every core loaded — and
    /// a run still reading as running when the wait ends counts as a start. Fifteen seconds is
    /// about twice the worst idle measurement.</summary>
    public static readonly TimeSpan VerificationWait = TimeSpan.FromSeconds(15);

    /// <summary>How long a demand start holds a result of zero before believing it. The scheduler
    /// writes zero on its way from the running marker to the program's exit code, so a zero read as
    /// a run ends is either. Measured worst case between the state settling and the result settling
    /// is 72 ms, so this is roughly seven times it, and it is paid only where the result is zero.
    /// </summary>
    public static readonly TimeSpan ResultSettle = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly StartupTaskOptions _options;
    private readonly ILogSink _log;
    private readonly string _executablePath;
    private readonly TaskService _service;

    public StartupTask(StartupTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TaskName);

        _options = options;
        _log = options.Log;
        _executablePath = options.ExecutablePath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable the task should start is unknown: no path was given and the process reports none.");
        _service = new TaskService();
    }

    public string TaskName => _options.TaskName;

    /// <summary>Who the task runs as. A delegate rather than a direct call so a test can make the
    /// one read in <see cref="Repair"/> that is not obviously a scheduler call fail, and prove the
    /// outcome carries it instead of a throw reaching the application's start-up path.</summary>
    internal Func<TaskIdentity> Identity { get; set; } = TaskIdentity.Current;

    /// <summary>Registered and enabled. A direct fetch by name rather than a walk of the folder,
    /// because this is read on every refresh of a tray menu.</summary>
    public bool IsEnabled
    {
        get
        {
            using ScheduledTask? task = Find();
            return task?.Enabled ?? false;
        }
    }

    public StartupTaskState Read()
    {
        using ScheduledTask? task = Find();
        return task is null ? StartupTaskState.Absent : StateOf(task);
    }

    /// <summary>Registers the power-safe elevated logon task, replacing any task of the name.</summary>
    public void Register()
    {
        TaskIdentity identity = Identity();
        using TaskDefinition definition = StartupTaskDefinition.Build(_service, _options, identity, _executablePath, enabled: true);
        RegisterDefinition(definition, identity);
        _log.Info($"Startup task '{TaskName}' registered for {identity.AccountName}.");
    }

    /// <exception cref="InvalidOperationException">No task of the name is registered. The user asked
    /// for a change, and a silent no-op would leave the menu showing one that did not happen.</exception>
    public void Enable() => SetEnabled(true);

    /// <inheritdoc cref="Enable"/>
    public void Disable() => SetEnabled(false);

    /// <summary>Removes the task. False when there was none.</summary>
    public bool Delete()
    {
        using ScheduledTask? task = Find();
        if (task is null) return false;

        _service.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
        _log.Info($"Startup task '{TaskName}' deleted.");
        return true;
    }

    /// <summary>Rewrites a task an older build registered so it carries the current settings and
    /// points at the current executable, keeping whether the user has it enabled. Never creates one.
    /// Never throws: everything that touches the scheduler or the current identity is inside a
    /// delegate whose failure is the outcome, and the state logged afterwards is a line rather than
    /// part of the answer.</summary>
    public StartupTaskRepairResult Repair()
    {
        StartupTaskRepairResult result = StartupTaskRepair.Run(
            exists: () =>
            {
                using ScheduledTask? task = Find();
                return task is not null;
            },
            deviations: () =>
            {
                using ScheduledTask task = Find() ?? throw new InvalidOperationException($"The startup task '{TaskName}' vanished during repair.");
                return StartupTaskDefinition.Deviations(task.Definition, _executablePath, _options.Arguments);
            },
            rewrite: () =>
            {
                bool enabled;
                using (ScheduledTask task = Find() ?? throw new InvalidOperationException($"The startup task '{TaskName}' vanished during repair."))
                    enabled = task.Enabled;

                // Read inside the delegate, where a failure is the RepairFailed outcome. Read
                // before the call it would be the one thing here that throws out of a repair.
                TaskIdentity identity = Identity();

                using TaskDefinition definition = StartupTaskDefinition.Build(_service, _options, identity, _executablePath, enabled);
                RegisterDefinition(definition, identity);
            },
            verify: _options.VerifyByDemandStart ? () => DemandStart(VerificationWait).Succeeded : null,
            _log);

        LogStateAfterRepair();
        return result;
    }

    /// <summary>Starts the task now and waits for the scheduler to report the run. This is the only
    /// read that proves the task can start the executable; existence and the enabled flag do not.
    /// The result code is read at every poll rather than once at the end, because the scheduler
    /// settles a run's state before its result and a code read at the moment the state settles can
    /// still be the launch's zero rather than the program's. A program that stays resident never
    /// ends its run, so the reading taken once the wait is over decides that case: a task still
    /// running then, whose run time has moved, started what it points at.</summary>
    /// <exception cref="InvalidOperationException">No task of the name is registered.</exception>
    public StartupTaskRunResult DemandStart(TimeSpan wait)
    {
        using ScheduledTask task = Find() ?? throw new InvalidOperationException($"The startup task '{TaskName}' is not registered, so it cannot be started.");

        DateTime before = task.LastRunTime;
        task.Run();

        DateTime deadline = DateTime.UtcNow + wait;
        DateTime? zeroSince = null;
        DateTime? lastRun = null;
        int? settled = null;

        while (DateTime.UtcNow < deadline)
        {
            DateTime run = task.LastRunTime;
            TaskState state = task.State;
            int result = task.LastTaskResult;

            // Ready alone means the run is over. A queued run has already moved the run time on
            // while the result still holds the previous run's, so anything short of Ready is a
            // reading of a run that has not finished.
            bool ended = run != before
                         && state == TaskState.Ready
                         && result != RunningResult
                         && result != NeverRunResult;
            if (!ended)
            {
                zeroSince = null;
                Thread.Sleep(PollInterval);
                continue;
            }

            if (result != 0)
            {
                lastRun = run;
                settled = result;
                break;
            }

            // Zero is what the scheduler writes between the running marker and the program's exit
            // code, so it counts only once it has held for the settle.
            zeroSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - zeroSince >= ResultSettle)
            {
                lastRun = run;
                settled = 0;
                break;
            }

            Thread.Sleep(PollInterval);
        }

        bool stillRunning = false;
        int? reported = settled;

        // Nothing settled inside the wait. One further reading decides, taken now rather than kept
        // from the last poll, so a run that ended with zero inside the final settle still reports as
        // a run that had not ended. There is no early return while the task runs: a program that ran
        // six seconds and then exited with a failure reads exactly like one that was about to exit
        // with zero, and only the end of the wait tells the two apart.
        if (settled is null)
        {
            DateTime run = task.LastRunTime;
            if (StartedAndStillRunning(task.State, run, before))
            {
                stillRunning = true;
                lastRun = run;
                reported = task.LastTaskResult;
            }
        }

        var outcome = new StartupTaskRunResult(settled is not null, lastRun, reported, stillRunning);
        _log.Info(DescribeRun(outcome, wait));
        return outcome;
    }

    /// <summary>Whether the reading taken when the wait ended is a program that started and is still
    /// up. The scheduler reports the task as running both where it launched the program and where it
    /// refused the start because an instance was already alive, and either says the task can run.
    /// Queued is not running: a run waiting to start has moved the run time on without starting
    /// anything, and a task that cannot start its program never reaches running at all — it goes
    /// straight back to ready carrying the error. The run time is read as well; on its own it proves
    /// nothing, because a refused start moves it without launching anything, but unmoved it says the
    /// scheduler recorded no attempt at all.</summary>
    internal static bool StartedAndStillRunning(TaskState state, DateTime lastRun, DateTime before) =>
        state == TaskState.Running && lastRun != before;

    public void Dispose() => _service.Dispose();

    private ScheduledTask? Find() => _service.GetTask(TaskName);

    // The state after a repair is a log line, not part of the outcome, so a scheduler that refuses
    // the read costs the line and nothing else. Repair promises never to throw.
    private void LogStateAfterRepair()
    {
        try
        {
            using ScheduledTask? after = Find();
            if (after is not null) _log.Info(Describe(StateOf(after)));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(StartupTask), ex);
        }
    }

    private void RegisterDefinition(TaskDefinition definition, TaskIdentity identity) =>
        _service.RootFolder.RegisterTaskDefinition(TaskName, definition, TaskCreation.CreateOrUpdate,
                                                   identity.Sid, null, TaskLogonType.InteractiveToken);

    private void SetEnabled(bool enabled)
    {
        using ScheduledTask task = Find()
            ?? throw new InvalidOperationException($"The startup task '{TaskName}' is not registered, so it cannot be {(enabled ? "enabled" : "disabled")}.");
        task.Enabled = enabled;
        _log.Info($"Startup task '{TaskName}' {(enabled ? "enabled" : "disabled")}.");
    }

    private static StartupTaskState StateOf(ScheduledTask task)
    {
        int result = task.LastTaskResult;
        bool ran = result != NeverRunResult;
        return new StartupTaskState(true, task.Enabled, ran ? task.LastRunTime : null, result, ran);
    }

    private string DescribeRun(StartupTaskRunResult outcome, TimeSpan wait)
    {
        if (outcome.Ran)
            return $"Startup task '{TaskName}' demand-started; result 0x{outcome.LastResult:X}.";
        if (!outcome.StillRunning)
            return $"Startup task '{TaskName}' demand-started but the run had not ended after {wait.TotalSeconds:0} s.";
        return outcome.LastResult == AlreadyRunningResult
            ? $"Startup task '{TaskName}' demand-started; an instance was already running, so the scheduler refused the start and the task counts as started."
            : $"Startup task '{TaskName}' demand-started; still running after {wait.TotalSeconds:0} s, so the task started what it points at and it stays resident.";
    }

    private string Describe(StartupTaskState state) =>
        $"Startup task '{TaskName}': registered, {(state.Enabled ? "enabled" : "disabled")}, " +
        (state.HasEverRun
            ? $"last run {state.LastRun?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} with result 0x{state.LastResult:X}."
            : "never run.");
}
