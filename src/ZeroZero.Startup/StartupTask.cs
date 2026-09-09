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

    /// <summary>How long a repair's verification waits for the demand-started run to end.</summary>
    public static readonly TimeSpan VerificationWait = TimeSpan.FromSeconds(30);

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
    /// still be the launch's zero rather than the program's.</summary>
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

        var outcome = new StartupTaskRunResult(settled is not null, lastRun, settled);
        _log.Info(settled is int code
            ? $"Startup task '{TaskName}' demand-started; result 0x{code:X}."
            : $"Startup task '{TaskName}' demand-started but the run had not ended after {wait.TotalSeconds:0} s.");
        return outcome;
    }

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

    private string Describe(StartupTaskState state) =>
        $"Startup task '{TaskName}': registered, {(state.Enabled ? "enabled" : "disabled")}, " +
        (state.HasEverRun
            ? $"last run {state.LastRun?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} with result 0x{state.LastResult:X}."
            : "never run.");
}
