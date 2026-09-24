using ZeroZero.Primitives;

namespace ZeroZero.Startup;

/// <summary>The decision behind keeping the watchdog task correct, over delegates, so it is testable
/// without a scheduler. A failure in any delegate is an outcome rather than an exception: this runs
/// at application start, where a throw would take the application down over the very task that
/// exists to bring it back.</summary>
public static class WatchdogEnsure
{
    /// <summary>The deviation reported where no task is registered at all. Unlike the logon task, an
    /// absent watchdog is written rather than left absent: it is the application's own backstop, not
    /// a choice the person made.</summary>
    public const string NotRegistered = "is not registered";

    /// <param name="mayRegister">Whether the executable may be written into a task at all. Null
    /// registers whatever runs.</param>
    /// <param name="withinRestartBound">Whether the probe has restarted the application too often to
    /// keep probing. Null leaves the restarts unbounded.</param>
    /// <param name="exists">Whether a task of the name is registered.</param>
    /// <param name="deviations">What differs from the intended task; empty when nothing does.</param>
    /// <param name="write">Registers the intended task, over any existing one.</param>
    /// <param name="stopProbing">Stops a registered task from probing again. Called only where
    /// <paramref name="withinRestartBound"/> says the bound is reached.</param>
    public static WatchdogEnsureResult Run(Func<bool>? mayRegister,
                                           Func<bool>? withinRestartBound,
                                           Func<bool> exists,
                                           Func<IReadOnlyList<string>> deviations,
                                           Action write,
                                           Action? stopProbing = null,
                                           ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(deviations);
        ArgumentNullException.ThrowIfNull(write);
        if (withinRestartBound is not null) ArgumentNullException.ThrowIfNull(stopProbing);
        log ??= NullLogSink.Instance;

        IReadOnlyList<string> found = [];
        try
        {
            if (mayRegister is not null && !mayRegister())
            {
                log.Info("Watchdog task not written: the running executable is not one this application registers from.");
                return new WatchdogEnsureResult(WatchdogEnsureOutcome.Skipped, found, null);
            }

            // Before anything is written: a task rewritten and then disabled in the same call would
            // report the rewrite as the outcome and hide the reason probing stopped.
            if (withinRestartBound is not null && !withinRestartBound())
            {
                stopProbing!();
                return new WatchdogEnsureResult(WatchdogEnsureOutcome.Stopped, found, null);
            }

            found = exists() ? deviations() : [NotRegistered];
            if (found.Count == 0)
                return new WatchdogEnsureResult(WatchdogEnsureOutcome.AlreadyCorrect, found, null);

            log.Info($"Watchdog task needs writing: {string.Join("; ", found)}.");
            write();
        }
        catch (Exception ex)
        {
            log.Error(nameof(WatchdogEnsure), ex);
            return new WatchdogEnsureResult(WatchdogEnsureOutcome.Failed, found, ex);
        }

        log.Info("Watchdog task registered.");
        return new WatchdogEnsureResult(WatchdogEnsureOutcome.Registered, found, null);
    }
}
