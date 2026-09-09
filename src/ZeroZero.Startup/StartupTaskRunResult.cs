namespace ZeroZero.Startup;

/// <summary>What a demand start came to.</summary>
/// <param name="Ran">The scheduler started the task and the run ended within the wait.</param>
/// <param name="LastRun">When the scheduler recorded the run as starting.</param>
/// <param name="LastResult">The exit code of the run where it ended; where it had not, the code the
/// scheduler carries while a task is running — <see cref="StartupTask.RunningResult"/>, or
/// <see cref="StartupTask.AlreadyRunningResult"/> where it refused the start.</param>
/// <param name="StillRunning">The task was running when the wait ended, having been started within
/// it. A programme that stays resident never exits, so this is what a successful start of one looks
/// like.</param>
public sealed record StartupTaskRunResult(bool Ran, DateTime? LastRun, int? LastResult, bool StillRunning)
{
    /// <summary>The task started its executable: either the executable is still running, or it ran
    /// to the end and exited with zero. Waiting for an exit code alone would fail every task that
    /// starts a program which stays resident, because such a program never exits.</summary>
    public bool Succeeded => StillRunning || (Ran && LastResult == 0);
}
