namespace ZeroZero.Lifecycle;

/// <summary>What the exit hook decided, and why.</summary>
public enum RelaunchDecision
{
    /// <summary>The exit was not asked for and the budget allows it: the executable is started again.</summary>
    Relaunch,

    /// <summary>The application marked the exit deliberate.</summary>
    DeliberateExit,

    /// <summary>Windows is logging the user off or shutting down.</summary>
    SessionEnding,

    /// <summary>The limiter has seen its budget of relaunches inside the window.</summary>
    LimitReached,
}
