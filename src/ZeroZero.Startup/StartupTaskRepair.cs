using ZeroZero.Primitives;

namespace ZeroZero.Startup;

/// <summary>The repair decision, over delegates, so the decision is testable without a scheduler.
/// The delegates are the only place a scheduler is touched, and a failure in any of them is an
/// outcome rather than an exception: this runs at application start, where a throw would take the
/// application down over a task it never needed to be running.</summary>
public static class StartupTaskRepair
{
    /// <param name="exists">Whether a task of the name is registered.</param>
    /// <param name="deviations">What differs from the intended task; empty when nothing does.</param>
    /// <param name="rewrite">Registers the intended task over the existing one.</param>
    /// <param name="verify">After a rewrite, whether the task started its executable and the run
    /// succeeded. Null to skip verification.</param>
    public static StartupTaskRepairResult Run(Func<bool> exists,
                                              Func<IReadOnlyList<string>> deviations,
                                              Action rewrite,
                                              Func<bool>? verify,
                                              ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(deviations);
        ArgumentNullException.ThrowIfNull(rewrite);
        log ??= NullLogSink.Instance;

        IReadOnlyList<string> found = [];
        try
        {
            if (!exists())
            {
                log.Info("Startup task is not registered; nothing to repair. Whether the application runs at logon is the user's choice.");
                return new StartupTaskRepairResult(StartupTaskRepairOutcome.NotRegistered, found, null);
            }

            found = deviations();
            if (found.Count == 0)
                return new StartupTaskRepairResult(StartupTaskRepairOutcome.AlreadyCorrect, found, null);

            log.Info($"Startup task needs repair: {string.Join("; ", found)}.");
            rewrite();
        }
        catch (Exception ex)
        {
            log.Error(nameof(StartupTaskRepair), ex);
            return new StartupTaskRepairResult(StartupTaskRepairOutcome.RepairFailed, found, ex);
        }

        if (verify is not null)
        {
            try
            {
                if (!verify())
                {
                    log.Error(nameof(StartupTaskRepair), new InvalidOperationException("The startup task was rewritten but a demand start did not end in a successful run."));
                    return new StartupTaskRepairResult(StartupTaskRepairOutcome.VerificationFailed, found, null);
                }
            }
            catch (Exception ex)
            {
                log.Error(nameof(StartupTaskRepair), ex);
                return new StartupTaskRepairResult(StartupTaskRepairOutcome.VerificationFailed, found, ex);
            }
        }

        log.Info("Startup task repaired.");
        return new StartupTaskRepairResult(StartupTaskRepairOutcome.Repaired, found, null);
    }
}
