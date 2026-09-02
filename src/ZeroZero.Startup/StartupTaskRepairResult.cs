namespace ZeroZero.Startup;

/// <param name="Outcome">What the repair did.</param>
/// <param name="Deviations">What was found wrong before any rewrite; empty when nothing was.</param>
/// <param name="Error">The exception behind a failed outcome, where there was one.</param>
public sealed record StartupTaskRepairResult(StartupTaskRepairOutcome Outcome, IReadOnlyList<string> Deviations, Exception? Error);
