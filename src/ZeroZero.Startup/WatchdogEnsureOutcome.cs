namespace ZeroZero.Startup;

/// <summary>What bringing the watchdog task up to date found and did.</summary>
public enum WatchdogEnsureOutcome
{
    /// <summary>The executable did not pass the application's own check on where it runs from, so
    /// nothing was written. A task pointing at build output would start stale binaries for as long
    /// as it survives.</summary>
    Skipped,

    /// <summary>The task is as it should be.</summary>
    AlreadyCorrect,

    /// <summary>The task was absent, or differed, and has been written.</summary>
    Registered,

    /// <summary>A read or the write failed. The result carries the exception, and the application
    /// runs this time with no backstop.</summary>
    Failed,
}
