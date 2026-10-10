namespace ZeroZero.Tray.WinUI;

/// <summary>The wait and the retry with every effect passed in, so the bounds are measured without a desktop.</summary>
internal static class TrayStartupSequence
{
    internal static readonly TimeSpan ShellPoll = TimeSpan.FromMilliseconds(250);

    // No ConfigureAwait(false): prepare and create run on the caller's UI thread.
    internal static async Task<TrayStartupResult> RunAsync(
        TrayStartupOptions options,
        Func<bool> isShellPresent,
        Action prepare,
        Action create,
        Action abandon,
        Func<TimeSpan, Task> delay,
        Func<TimeSpan> elapsed)
    {
        while (!isShellPresent())
        {
            TimeSpan left = options.ShellWait - elapsed();
            if (left <= TimeSpan.Zero)
                return new TrayStartupResult(TrayStartupOutcome.ShellNeverAppeared, 0, elapsed(), null);
            await delay(left < ShellPoll ? left : ShellPoll);
        }

        prepare();

        Exception? last = null;
        for (int attempt = 1; attempt <= options.CreateAttempts; attempt++)
        {
            if (attempt > 1) await delay(options.RetryDelay);
            try
            {
                create();
                return new TrayStartupResult(TrayStartupOutcome.Created, attempt, elapsed(), null);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        abandon();
        return new TrayStartupResult(TrayStartupOutcome.CreationFailed, options.CreateAttempts, elapsed(), last);
    }
}
