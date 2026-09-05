namespace ZeroZero.Lifecycle;

/// <summary>The one lock that says this process is the running instance. Taken before any window
/// or tray icon exists, on the thread that lives as long as the process, and held until the process
/// dies. Nothing releases it: a release while the process still runs is exactly the state — two
/// instances, two tray icons — the lock exists to prevent.</summary>
public static class SingleInstanceLock
{
    private static readonly Lock Gate = new();

    // Rooted for the life of the process. A mutex the collector finalises is a mutex released.
    private static Mutex? _held;
    private static string? _heldName;
    private static SingleInstanceOutcome _heldOutcome;

    public static bool IsHeld
    {
        get { lock (Gate) return _held is not null; }
    }

    /// <summary>Takes the named mutex, waiting up to <paramref name="wait"/> for a previous instance
    /// to let go — zero for a fresh launch, longer for a relaunch racing the exit of the process that
    /// spawned it. Reports which of the four outcomes happened, so an application can say in its log
    /// whether it took a free name or one a dead instance left behind. A second call under the same
    /// name reports the outcome the first one had.</summary>
    /// <exception cref="InvalidOperationException">The process already holds a lock under another
    /// name. A process is one instance of one product.</exception>
    public static SingleInstanceOutcome Acquire(string mutexName, TimeSpan wait)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        lock (Gate)
        {
            if (_held is not null)
            {
                if (string.Equals(_heldName, mutexName, StringComparison.Ordinal)) return _heldOutcome;
                throw new InvalidOperationException($"This process already holds the lock '{_heldName}' and cannot also hold '{mutexName}'.");
            }

            SingleInstanceOutcome outcome = Open(mutexName, wait, out Mutex? mutex);
            if (mutex is null) return outcome;

            _held = mutex;
            _heldName = mutexName;
            _heldOutcome = outcome;
            return outcome;
        }
    }

    /// <summary>The same acquisition, as the answer most callers act on. True when this process now
    /// holds the lock; false when another instance holds it or the name is not this process's to
    /// take. <see cref="Acquire"/> says which.</summary>
    /// <exception cref="InvalidOperationException">The process already holds a lock under another
    /// name.</exception>
    public static bool TryAcquire(string mutexName, TimeSpan wait) => Acquire(mutexName, wait).IsTaken();

    /// <summary>The acquisition alone, handing back the owned mutex or null. Ownership belongs to
    /// the calling thread.</summary>
    internal static SingleInstanceOutcome Open(string mutexName, TimeSpan wait, out Mutex? mutex)
    {
        mutex = null;

        Mutex candidate;
        try
        {
            candidate = new Mutex(initiallyOwned: false, mutexName);
        }
        catch (UnauthorizedAccessException)
        {
            // The name exists under rights this token does not have — another session's instance, or
            // one running elevated. That is a refusal: the alternative is a second instance.
            return SingleInstanceOutcome.RefusedDenied;
        }

        bool abandoned = false;
        bool acquired;
        try
        {
            acquired = candidate.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died without releasing. The wait has granted ownership all the same.
            acquired = true;
            abandoned = true;
        }
        catch
        {
            // A name this process cannot wait on at all is the application's own error, not a second
            // instance, and it must be seen rather than read as an ordinary refusal.
            candidate.Dispose();
            throw;
        }

        if (!acquired)
        {
            candidate.Dispose();
            return SingleInstanceOutcome.RefusedHeld;
        }

        mutex = candidate;
        return abandoned ? SingleInstanceOutcome.TakenAbandoned : SingleInstanceOutcome.TakenFree;
    }
}
