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

    public static bool IsHeld
    {
        get { lock (Gate) return _held is not null; }
    }

    /// <summary>Takes the named mutex, waiting up to <paramref name="wait"/> for a previous instance
    /// to let go — zero for a fresh launch, longer for a relaunch racing the exit of the process that
    /// spawned it. A mutex its holder abandoned counts as taken: the holder is dead, which is the
    /// case relaunch exists for. True when this process now holds the lock; false when another
    /// instance still holds it after the wait.</summary>
    /// <exception cref="InvalidOperationException">The process already holds a lock under another
    /// name. A process is one instance of one product.</exception>
    public static bool TryAcquire(string mutexName, TimeSpan wait)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        lock (Gate)
        {
            if (_held is not null)
            {
                if (string.Equals(_heldName, mutexName, StringComparison.Ordinal)) return true;
                throw new InvalidOperationException($"This process already holds the lock '{_heldName}' and cannot also hold '{mutexName}'.");
            }

            Mutex? mutex = Open(mutexName, wait);
            if (mutex is null) return false;

            _held = mutex;
            _heldName = mutexName;
            return true;
        }
    }

    /// <summary>The acquisition alone, handing back the owned mutex or null. Ownership belongs to
    /// the calling thread.</summary>
    internal static Mutex? Open(string mutexName, TimeSpan wait)
    {
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died without releasing. The wait has granted ownership all the same.
            acquired = true;
        }

        if (acquired) return mutex;

        mutex.Dispose();
        return null;
    }
}
