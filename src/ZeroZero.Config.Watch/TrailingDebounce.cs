namespace ZeroZero.Config.Watch;

/// <summary>Runs one action once a burst of signals has stopped arriving.</summary>
/// <remarks>
/// <para>Every signal restarts the quiet window, so a run happens on the trailing edge and a single
/// save that reaches the operating system as several notifications is examined once. A signal that
/// arrives while a run is under way starts a fresh window rather than being lost.</para>
/// <para>The window is measured on an injected <see cref="TimeProvider"/>, which is what lets the
/// collapsing be tested by moving a clock rather than by waiting: a test that waits a fixed period
/// measures the machine it runs on, and passes or fails by how loaded that machine is.</para>
/// <para>Internal on purpose. A general debouncer belongs in the primitives package, not published
/// out of a settings one; it moves there the first time a second consumer wants it.</para>
/// </remarks>
internal sealed class TrailingDebounce : IDisposable
{
    private readonly Lock _gate = new();
    private readonly TimeSpan _quiet;
    private readonly Action _run;
    private readonly ITimer _timer;

    private bool _pending;
    private bool _disposed;

    internal TrailingDebounce(TimeSpan quiet, TimeProvider time, Action run)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quiet, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(run);

        _quiet = quiet;
        _run = run;
        _timer = time.CreateTimer(static state => ((TrailingDebounce)state!).Elapsed(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Records a signal and restarts the quiet window.</summary>
    internal void Signal()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _pending = true;
            _timer.Change(_quiet, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _pending = false;
        }

        _timer.Dispose();
    }

    private void Elapsed()
    {
        lock (_gate)
        {
            if (_disposed || !_pending) return;
            _pending = false;
        }

        // Outside the lock: the run re-reads a file and raises events, and a signal arriving during
        // it must be able to arm the next window rather than block behind it.
        _run();
    }
}
