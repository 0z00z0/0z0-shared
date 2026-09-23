using Microsoft.Win32.TaskScheduler;
using ZeroZero.Primitives;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace ZeroZero.Startup;

/// <summary>
/// The scheduled task that starts the application again when its process is gone — the one backstop
/// against any way the process can die, since nothing inside a dying process restarts it. It probes
/// on a short repeating interval, and again just after the workstation unlocks and just after the
/// machine resumes, so a process killed while the machine was asleep or locked comes back in
/// seconds rather than at the next tick.
/// </summary>
/// <remarks>
/// Unlike the logon task this one is the application's own responsibility: <see cref="Ensure"/>
/// writes it whether or not the person has chosen to start at logon, and writes it again whenever
/// it has drifted. Two things stay with the application: the single-instance gate that makes a
/// probe finding a live process exit at once, and calling <see cref="WatchdogHold.Hold"/> when the
/// person exits on purpose.
/// </remarks>
public sealed class WatchdogTask : IDisposable
{
    private readonly WatchdogTaskOptions _options;
    private readonly ILogSink _log;
    private readonly string _executablePath;
    private readonly TaskService _service;

    public WatchdogTask(WatchdogTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TaskName);

        _options = options;
        _log = options.Log;
        _executablePath = options.ExecutablePath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable the watchdog should start is unknown: no path was given and the process reports none.");
        _service = new TaskService();
        Hold = new WatchdogHold(options.HoldMarkerPath, options.Log);
    }

    public string TaskName => _options.TaskName;

    /// <summary>The record of a deliberate exit, at the path the options name.</summary>
    public WatchdogHold Hold { get; }

    /// <summary>Who the task runs as. A delegate rather than a direct call so a test can make the
    /// one read that is not obviously a scheduler call fail, and prove the outcome carries it.</summary>
    internal Func<TaskIdentity> Identity { get; set; } = TaskIdentity.Current;

    /// <summary>Registers the watchdog task, or rewrites one that has drifted, and leaves a correct
    /// one alone. Called on every start. Never throws: a failure costs the backstop for this run and
    /// must not cost the application its start.</summary>
    public WatchdogEnsureResult Ensure() =>
        WatchdogEnsure.Run(
            mayRegister: _options.RegisterWhen is { } gate ? () => gate(_executablePath) : null,
            exists: () =>
            {
                using ScheduledTask? task = Find();
                return task is not null;
            },
            deviations: () =>
            {
                using ScheduledTask task = Find() ?? throw new InvalidOperationException($"The watchdog task '{TaskName}' vanished while it was being read.");
                return WatchdogTaskDefinition.Deviations(task.Definition, _options, _executablePath);
            },
            write: () =>
            {
                // Read inside the delegate, where a failure is the Failed outcome rather than a
                // throw out of a start-up path.
                TaskIdentity identity = Identity();
                using TaskDefinition definition = WatchdogTaskDefinition.Build(_service, _options, identity, _executablePath);
                _service.RootFolder.RegisterTaskDefinition(TaskName, definition, TaskCreation.CreateOrUpdate,
                                                           identity.Sid, null, TaskLogonType.InteractiveToken);
            },
            _log);

    /// <summary>Removes the task. False when there was none. The installer's uninstall step owns
    /// this on the way out; nothing in the application's own running removes its backstop.</summary>
    public bool Delete()
    {
        using ScheduledTask? task = Find();
        if (task is null) return false;

        _service.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
        _log.Info($"Watchdog task '{TaskName}' deleted.");
        return true;
    }

    public void Dispose() => _service.Dispose();

    private ScheduledTask? Find() => _service.GetTask(TaskName);
}
