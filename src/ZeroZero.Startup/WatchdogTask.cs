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
/// <para>The constructor only checks its arguments. The scheduler connection, the executable path,
/// the marker path and the two task names are all resolved inside <see cref="Ensure"/>, where a
/// refusal is the <see cref="WatchdogEnsureOutcome.Failed"/> outcome rather than a throw out of an
/// application's start-up path.</para>
/// </remarks>
public sealed class WatchdogTask : IDisposable
{
    private readonly WatchdogTaskOptions _options;
    private readonly ILogSink _log;

    private string? _executablePath;
    private TaskService? _service;
    private WatchdogHold? _hold;
    private WatchdogRestartLimiter? _limiter;

    public WatchdogTask(WatchdogTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TaskName);

        _options = options;
        _log = options.Log;
    }

    public string TaskName => _options.TaskName;

    /// <summary>The record of a deliberate exit, at the path the options name. Made on first use, so
    /// a path the file system refuses is a failed <see cref="Ensure"/> rather than a constructor
    /// that throws.</summary>
    public WatchdogHold Hold => _hold ??= new WatchdogHold(_options.HoldMarkerPath, _log);

    /// <summary>The bound on the probe's own restarts, counted beside the hold marker.</summary>
    public WatchdogRestartLimiter Restarts =>
        _limiter ??= new WatchdogRestartLimiter(StateDirectory, _options.Interval, _log);

    /// <summary>Who the task runs as. A delegate rather than a direct call so a test can make the
    /// one read that is not obviously a scheduler call fail, and prove the outcome carries it.</summary>
    internal Func<TaskIdentity> Identity { get; set; } = TaskIdentity.Current;

    /// <summary>Registers the watchdog task, or rewrites one that has drifted, and leaves a correct
    /// one alone. Called on every start. Never throws: a failure costs the backstop for this run and
    /// must not cost the application its start.</summary>
    public WatchdogEnsureResult Ensure()
    {
        WatchdogEnsureResult result = WatchdogEnsure.Run(
            mayRegister: () =>
            {
                // Everything that can fail is read here rather than in the constructor, so a
                // refusal is this call's outcome.
                Prepare();
                return _options.RegisterWhen is null || _options.RegisterWhen(_executablePath!);
            },
            withinRestartBound: () => Restarts.RecordStart(_options.StartCause),
            exists: () =>
            {
                using ScheduledTask? task = Find();
                return task is not null;
            },
            deviations: () =>
            {
                using ScheduledTask task = Find() ?? throw new InvalidOperationException($"The watchdog task '{TaskName}' vanished while it was being read.");
                return WatchdogTaskDefinition.Deviations(task.Definition, _options, _executablePath!, Identity());
            },
            write: () =>
            {
                TaskIdentity identity = Identity();
                using TaskDefinition definition = WatchdogTaskDefinition.Build(Service, _options, identity, _executablePath!);
                Service.RootFolder.RegisterTaskDefinition(TaskName, definition, TaskCreation.CreateOrUpdate,
                                                          identity.Sid, null, TaskLogonType.InteractiveToken);
            },
            stopProbing: StopProbing,
            _log);

        // The install path is written here and nowhere else: a drift line goes out at information
        // level on an ordinary start, and a path in it says where the application lives to anyone
        // reading the log.
        if (result.Outcome == WatchdogEnsureOutcome.Failed)
            _log.Error(nameof(WatchdogTask), new InvalidOperationException($"The watchdog task '{TaskName}' was not written for '{_executablePath ?? _options.ExecutablePath}'."));

        return result;
    }

    /// <summary>Removes the task. False when there was none. The installer's uninstall step owns
    /// this on the way out; nothing in the application's own running removes its backstop.</summary>
    public bool Delete()
    {
        Prepare();
        using ScheduledTask? task = Find();
        if (task is null) return false;

        Service.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
        _log.Info($"Watchdog task '{TaskName}' deleted.");
        return true;
    }

    public void Dispose() => _service?.Dispose();

    private TaskService Service => _service ??= new TaskService();

    /// <summary>The folder the watchdog keeps its state in: the hold marker's own. A marker named
    /// with no folder at all leaves the process's working directory, which for a task-started
    /// process is the executable's.</summary>
    private string StateDirectory =>
        Path.GetDirectoryName(_options.HoldMarkerPath) is { Length: > 0 } folder ? folder : ".";

    private void Prepare()
    {
        if (_options.LogonTaskName is { Length: > 0 } logon
            && string.Equals(logon, _options.TaskName, StringComparison.OrdinalIgnoreCase))
        {
            // Scheduler names are case-insensitive, so writing one would replace the other and the
            // person's choice about starting at logon would go with it.
            throw new InvalidOperationException(
                $"The watchdog task and the logon task are both named '{_options.TaskName}', so neither is written.");
        }

        _executablePath ??= _options.ExecutablePath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable the watchdog should start is unknown: no path was given and the process reports none.");

        // Touched so a marker path the file system refuses fails here rather than at the exit that
        // wanted to record itself.
        _ = Hold;
        _ = Restarts;
        _ = Service;
    }

    private void StopProbing()
    {
        using ScheduledTask? task = Find();
        if (task is null) return;
        task.Enabled = false;
    }

    private ScheduledTask? Find() => Service.GetTask(TaskName);
}
