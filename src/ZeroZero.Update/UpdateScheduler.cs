using ZeroZero.Primitives;

namespace ZeroZero.Update;

/// <summary>Runs the check after an initial delay and then at an interval, one run at a time, for
/// as long as the scheduler lives. Counted from process start and never persisted: an application
/// up for a day checks once a day, and one restarted every hour checks once an hour.</summary>
public sealed class UpdateScheduler : IDisposable
{
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task> _check;
    private readonly ILogSink _log;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private int _runs;

    public UpdateScheduler(TimeSpan initialDelay, TimeSpan interval, Func<CancellationToken, Task> check, ILogSink? log = null)
    {
        if (initialDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(initialDelay), initialDelay, "The initial delay cannot be negative.");
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The interval must be longer than zero.");
        ArgumentNullException.ThrowIfNull(check);
        _initialDelay = initialDelay;
        _interval = interval;
        _check = check;
        _log = log ?? NullLogSink.Instance;
    }

    /// <summary>How many checks have run to completion, thrown or not.</summary>
    public int Runs => Volatile.Read(ref _runs);

    /// <summary>Once; a second call changes nothing.</summary>
    public void Start()
    {
        _loop ??= Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        CancellationToken token = _stop.Token;
        try
        {
            await Task.Delay(_initialDelay, token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _check(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // A check that throws is a check that failed; the schedule goes on.
                    _log.Error(nameof(UpdateScheduler), ex);
                }
                finally
                {
                    Interlocked.Increment(ref _runs);
                }
                await Task.Delay(_interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
