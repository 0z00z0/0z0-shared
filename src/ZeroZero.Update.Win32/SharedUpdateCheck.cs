namespace ZeroZero.Update.Win32;

/// <summary>
/// One update check at a time for every flow that holds it. A run arriving while a check is in
/// flight is handed that check and reads its result, whichever flow started it, rather than starting
/// a second request. An application constructs one over its service and hands it to every
/// <see cref="UpdateFlow"/> it builds, through <see cref="UpdateFlowOptions.SharedCheck"/>, and to
/// <see cref="UnattendedUpdatePolicy"/> through <see cref="UnattendedUpdateOptions.SharedCheck"/>.
/// </summary>
/// <remarks>
/// Only the check is shared. What a run does with the result — speak, ask or stay quiet — is decided
/// by that run's own trigger and its own flow's prompts, so a silent run stays silent whichever run
/// it joined, and a manual run still reports in its own window when it joined a silent one.
/// </remarks>
public sealed class SharedUpdateCheck
{
    private readonly Lock _gate = new();
    private Task<UpdateCheckResult>? _check;

    public SharedUpdateCheck(IUpdateService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Service = service;
    }

    /// <summary>What every check runs against. A flow built over another service refuses this
    /// check: one answer stands for one service.</summary>
    internal IUpdateService Service { get; }

    /// <summary>The check every holder shares. One arriving while a check is in flight is handed
    /// that check and reads its result, rather than starting a second one or being refused. The
    /// slot is cleared as the check ends, before any caller resumes, so the next request starts a
    /// fresh check rather than reading an answer that has already been given.</summary>
    /// <remarks>The request runs under no caller's token, and every caller waits under its own: a
    /// cancelled caller stops only its own wait, and the request runs on to its own timeout for
    /// whoever else is waiting or joins it.</remarks>
    internal Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Join().WaitAsync(cancellationToken);

    private Task<UpdateCheckResult> Join()
    {
        lock (_gate)
        {
            if (_check is { IsCompleted: false } running) return running;

            Task<UpdateCheckResult> fresh = CheckAndClearAsync();
            // A check that finished before returning has already cleared the slot; storing it
            // would leave a finished answer where the next caller looks for one in flight.
            _check = fresh.IsCompleted ? null : fresh;
            return fresh;
        }
    }

    private async Task<UpdateCheckResult> CheckAndClearAsync()
    {
        try
        {
            return await Service.CheckAsync(CancellationToken.None);
        }
        finally
        {
            lock (_gate) _check = null;
        }
    }
}
