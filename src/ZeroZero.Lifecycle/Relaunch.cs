namespace ZeroZero.Lifecycle;

/// <summary>What a relaunched process is told, and what it should allow the process that spawned
/// it.</summary>
public static class Relaunch
{
    /// <summary>The argument the exit hook starts the executable with, so the new process can tell
    /// a relaunch from a launch and choose its wait for the lock accordingly.</summary>
    public const string Argument = "--relaunched";

    /// <summary>The wait a relaunched process gives its parent to finish exiting. The mutex is
    /// released only when the parent's own exit handlers have run, so a relaunch that gives up on
    /// the lock at once finds it still held, exits, and leaves no tray at all.</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(10);

    public static bool WasRelaunched(IEnumerable<string> commandLineArguments)
    {
        ArgumentNullException.ThrowIfNull(commandLineArguments);
        return commandLineArguments.Contains(Argument, StringComparer.Ordinal);
    }
}
