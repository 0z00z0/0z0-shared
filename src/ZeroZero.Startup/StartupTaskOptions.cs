using ZeroZero.Primitives;

namespace ZeroZero.Startup;

/// <summary>What the application supplies about its logon task.</summary>
public sealed class StartupTaskOptions
{
    /// <summary>The task's name in the scheduler's root folder: its public identity, which the
    /// installer's registration and uninstall must match.</summary>
    public required string TaskName { get; init; }

    public string Description { get; init; } = "";

    /// <summary>What the task starts. The running executable when null.</summary>
    public string? ExecutablePath { get; init; }

    public string Arguments { get; init; } = "";

    /// <summary>After a repair, start the task on demand and wait for the scheduler to report the
    /// run. A task that is registered and enabled but has never started the executable reports
    /// healthy on every read; this is the one check that tells the two apart.</summary>
    public bool VerifyByDemandStart { get; init; }

    public ILogSink Log { get; init; } = NullLogSink.Instance;
}
