namespace ZeroZero.Config.Watch.Tests;

/// <summary>A clock whose timers fire when the test says so, whatever they were armed for.</summary>
/// <remarks>A controllable clock moves time and fires what is due, which cannot reach one case a real
/// timer reaches: a callback already queued when the timer was re-armed is still delivered, so an
/// elapse can arrive with nothing pending. This provider delivers that callback on demand.</remarks>
internal sealed class ManualTimerProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    /// <summary>Delivers one callback to every timer this provider has handed out.</summary>
    public void Fire()
    {
        ManualTimer[] timers;
        lock (_timers) timers = [.. _timers];

        foreach (var timer in timers) timer.Fire();
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        public void Fire()
        {
            if (!_disposed) callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
