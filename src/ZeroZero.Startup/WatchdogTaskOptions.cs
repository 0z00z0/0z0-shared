using ZeroZero.Primitives;

namespace ZeroZero.Startup;

/// <summary>What the application supplies about its watchdog task — the scheduled task that starts
/// the application again when its process is gone.</summary>
public sealed class WatchdogTaskOptions
{
    /// <summary>The task's name in the scheduler's root folder, and its public identity: the
    /// installer's uninstall step has to remove a task of this name.</summary>
    public required string TaskName { get; init; }

    public string Description { get; init; } = "";

    /// <summary>What the task starts. The running executable when null.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>The argument the task passes, which tells the started process that it is a watchdog
    /// relaunch rather than a person starting the application. The application decides what to do
    /// with it; an empty string passes none.</summary>
    public string Arguments { get; init; } = "";

    /// <summary>How often the probe runs. A short interval bounds how long a killed process stays
    /// gone, and the unlock and resume triggers close the window where a kill during sleep would
    /// cost the whole interval.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long after the workstation unlocks the probe runs. The shell is still settling
    /// at the moment of the unlock itself.</summary>
    public TimeSpan UnlockDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long after the machine reports a completed resume the probe runs.</summary>
    public TimeSpan ResumeDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The file whose presence tells a probe that the person exited on purpose. Required:
    /// where an application's data lives is the application's own answer, and a watchdog with
    /// nowhere to record a deliberate exit starts the application again against the person's
    /// choice.</summary>
    public required string HoldMarkerPath { get; init; }

    /// <summary>Whether the executable about to be written into the task may be. A development build
    /// registered here resurrects stale binaries for as long as the task survives, so an application
    /// installed to a known location passes a check for it. Null registers whatever runs.</summary>
    public Func<string, bool>? RegisterWhen { get; init; }

    public ILogSink Log { get; init; } = NullLogSink.Instance;
}
