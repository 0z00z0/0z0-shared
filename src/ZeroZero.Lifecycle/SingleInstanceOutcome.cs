namespace ZeroZero.Lifecycle;

/// <summary>How the single-instance lock answered. Taken and refused are two outcomes each, because
/// "took a free name" and "the previous holder died" are different facts about the machine, and so
/// are "another instance holds it" and "the name is not this token's to take".</summary>
public enum SingleInstanceOutcome
{
    /// <summary>Nobody held the name. This process is the first instance.</summary>
    TakenFree,

    /// <summary>The previous holder died without releasing, and the wait granted ownership all the
    /// same. This process is the instance, and the one before it did not exit cleanly.</summary>
    TakenAbandoned,

    /// <summary>Another instance still held the name when the wait ran out.</summary>
    RefusedHeld,

    /// <summary>The name exists and this process may not open it — another session's instance, or
    /// one holding it with rights this token does not have. Refused rather than taken: opening it
    /// is what would put a second instance on the machine.</summary>
    RefusedDenied,
}

public static class SingleInstanceOutcomeExtensions
{
    /// <summary>Whether the process now holds the lock. The two taken outcomes differ in what they
    /// say about the previous instance, never in whether this one may run.</summary>
    public static bool IsTaken(this SingleInstanceOutcome outcome) =>
        outcome is SingleInstanceOutcome.TakenFree or SingleInstanceOutcome.TakenAbandoned;
}
