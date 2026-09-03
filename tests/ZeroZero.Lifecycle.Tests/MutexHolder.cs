namespace ZeroZero.Lifecycle.Tests;

/// <summary>Another instance, played by a thread of its own: mutex ownership belongs to a thread,
/// so a second holder in the same process must be a second thread. It takes the named mutex on
/// construction and keeps it until told to let go — by releasing, or by ending while still owning
/// it, which is what a process that died looks like to the next one.</summary>
internal sealed class MutexHolder : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _held = new();
    private readonly ManualResetEventSlim _letGo = new();
    private readonly bool _abandon;
    private Mutex? _mutex;

    public bool Acquired { get; private set; }

    public MutexHolder(string name, bool abandon = false)
    {
        _abandon = abandon;
        _thread = new Thread(() =>
        {
            _mutex = new Mutex(initiallyOwned: false, name);
            Acquired = _mutex.WaitOne(0);
            _held.Set();
            _letGo.Wait();
            if (!_abandon)
            {
                // Nothing to release where the wait failed; releasing anyway throws on this thread
                // and takes the process down with it.
                if (Acquired) _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        })
        { IsBackground = true };
        _thread.Start();
        _held.Wait();
    }

    public void LetGo()
    {
        _letGo.Set();
        _thread.Join();
    }

    public void Dispose()
    {
        if (!_letGo.IsSet) LetGo();
    }
}
