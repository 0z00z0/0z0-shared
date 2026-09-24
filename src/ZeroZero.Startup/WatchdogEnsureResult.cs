namespace ZeroZero.Startup;

/// <param name="Outcome">What the check did.</param>
/// <param name="Deviations">What was found wrong before any write; empty when nothing was, and the
/// single entry "is not registered" where there was no task at all.</param>
/// <param name="Error">The exception behind a failed outcome, where there was one.</param>
public sealed record WatchdogEnsureResult(WatchdogEnsureOutcome Outcome, IReadOnlyList<string> Deviations, Exception? Error);
