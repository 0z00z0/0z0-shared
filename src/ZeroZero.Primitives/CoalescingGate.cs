namespace ZeroZero.Primitives;

/// <summary>Collapses a burst of signals into at most one in-flight run plus one trailing run. Tracks
/// only the running/pending flags, so the coalescing decision is testable without threads.</summary>
public sealed class CoalescingGate
{
    private readonly Lock _lock = new();
    private bool _running;
    private bool _pending;

    /// <summary>Records a signal, returning true only to the caller that must start the loop; a signal
    /// arriving while one runs returns false but arms a trailing pass.</summary>
    public bool Signal()
    {
        lock (_lock)
        {
            _pending = true;
            if (_running) return false;
            _running = true;
            return true;
        }
    }

    public void BeginPass()
    {
        lock (_lock) _pending = false;
    }

    /// <summary>True to run another pass; otherwise clears the running flag and ends the loop.</summary>
    public bool ShouldRepeat()
    {
        lock (_lock)
        {
            if (_pending) return true;
            _running = false;
            return false;
        }
    }
}
