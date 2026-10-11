namespace ZeroZero.Mqtt;

/// <summary>What cuts the maintain loop's wait short: a wake, or the link going down.</summary>
internal sealed class MqttLoopSignals
{
    private volatile TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Ends only the long poll of a live session; armed before each connect attempt, so an earlier
    // drop says nothing about the session that attempt starts.
    private volatile TaskCompletionSource _drop = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Waits up to <paramref name="delay"/>, early on a wake. False on cancel.</summary>
    /// <param name="wakeOnDrop">True only for the poll of a live session. Honoured in a backoff wait,
    /// the loop's own drop after a failed round would cancel the backoff and retry continuously.</param>
    public async Task<bool> DelayOrWake(TimeSpan delay, CancellationToken ct, bool wakeOnDrop = false)
    {
        var wake = _wake.Task;
        var drop = _drop.Task;
        // Linked source so a winning wake cancels the losing delay rather than abandoning its timer.
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var delayTask = Task.Delay(delay, delayCts.Token);
            var winner = wakeOnDrop
                ? await Task.WhenAny(delayTask, wake, drop).ConfigureAwait(false)
                : await Task.WhenAny(delayTask, wake).ConfigureAwait(false);

            if (winner != delayTask)
            {
                await delayCts.CancelAsync().ConfigureAwait(false);
                try { await delayTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
                // Re-arm whichever fired. A signal racing the swap costs at worst one poll interval.
                if (wake.IsCompleted)
                    _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (drop.IsCompleted) ArmDrop();
            }
            else
                await winner.ConfigureAwait(false);   // observe cancellation raised by the delay

            return !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException) { return false; }
    }

    public void Wake() => _wake.TrySetResult();

    public void Drop() => _drop.TrySetResult();

    public void ArmDrop() =>
        _drop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
}
