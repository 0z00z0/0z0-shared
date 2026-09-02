namespace ZeroZero.Startup;

/// <summary>What the scheduler says about the task. <paramref name="HasEverRun"/> is the field
/// that matters: a task can exist and be enabled and never once have started the executable, and
/// the two former facts say nothing about the third.</summary>
/// <param name="LastRun">When the scheduler last started the task; null when it never has.</param>
/// <param name="LastResult">The exit code of the last run, or the scheduler's own code — null when
/// the task does not exist.</param>
public sealed record StartupTaskState(bool Exists, bool Enabled, DateTime? LastRun, int? LastResult, bool HasEverRun)
{
    public static readonly StartupTaskState Absent = new(false, false, null, null, false);
}
