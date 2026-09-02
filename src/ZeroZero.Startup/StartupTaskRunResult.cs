namespace ZeroZero.Startup;

/// <summary>What a demand start came to.</summary>
/// <param name="Ran">The scheduler started the task and the run ended within the wait.</param>
/// <param name="LastResult">The exit code of the run, where it ran.</param>
public sealed record StartupTaskRunResult(bool Ran, DateTime? LastRun, int? LastResult)
{
    /// <summary>The task started its executable and the executable exited with zero.</summary>
    public bool Succeeded => Ran && LastResult == 0;
}
