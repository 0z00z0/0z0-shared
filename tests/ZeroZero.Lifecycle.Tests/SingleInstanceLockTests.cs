using System.Diagnostics;
using Xunit;
using ZeroZero.Lifecycle;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>Real named mutexes under names no application uses, with the other instance played by
/// a thread of its own. The acquisition is tested through the internal seam that hands the mutex
/// back, so each test can release what it took; the process-lifetime holding is tested once,
/// through the public path, and that lock stays held for the rest of the run.</summary>
public class SingleInstanceLockTests
{
    private static string DisposableName() => @"Local\ZeroZero.Lifecycle.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void AFreeNameIsTakenAtOnce()
    {
        using Mutex? taken = SingleInstanceLock.Open(DisposableName(), TimeSpan.Zero);

        Assert.NotNull(taken);
        taken.ReleaseMutex();
    }

    [Fact]
    public void ANameAnotherInstanceHoldsIsRefusedOnceTheWaitIsUp()
    {
        string name = DisposableName();
        using var other = new MutexHolder(name);
        Assert.True(other.Acquired);

        var stopwatch = Stopwatch.StartNew();
        using Mutex? taken = SingleInstanceLock.Open(name, TimeSpan.FromMilliseconds(300));
        stopwatch.Stop();

        Assert.Null(taken);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(250), $"Gave up after {stopwatch.ElapsedMilliseconds} ms without waiting.");
    }

    [Fact]
    public void AnInstanceThatLetsGoDuringTheWaitIsWaitedFor()
    {
        string name = DisposableName();
        using var other = new MutexHolder(name);
        Assert.True(other.Acquired);
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            other.LetGo();
        });

        using Mutex? taken = SingleInstanceLock.Open(name, TimeSpan.FromSeconds(10));

        Assert.NotNull(taken);
        taken.ReleaseMutex();
    }

    [Fact]
    public void AMutexItsHolderAbandonedCountsAsTaken()
    {
        string name = DisposableName();
        // A handle of the test's own keeps the kernel object alive across the holder's death, so the
        // next wait meets an abandoned mutex rather than a fresh one.
        using var keepAlive = new Mutex(initiallyOwned: false, name);
        using (var other = new MutexHolder(name, abandon: true))
        {
            Assert.True(other.Acquired);
            other.LetGo();
        }

        using Mutex? taken = SingleInstanceLock.Open(name, TimeSpan.Zero);

        Assert.NotNull(taken);
        taken.ReleaseMutex();
    }

    [Fact]
    public async Task TheProcessHoldsOneLockForItsLifetime()
    {
        string name = DisposableName();

        Assert.True(SingleInstanceLock.TryAcquire(name, TimeSpan.Zero));
        Assert.True(SingleInstanceLock.IsHeld);

        // Another instance, on another thread, finds the name taken with no wait at all.
        bool free = await Task.Run(() =>
        {
            using var probe = new Mutex(initiallyOwned: false, name);
            bool got = probe.WaitOne(0);
            if (got) probe.ReleaseMutex();
            return got;
        });
        Assert.False(free);

        // The same name again is the lock already held; another name is a second product.
        Assert.True(SingleInstanceLock.TryAcquire(name, TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => SingleInstanceLock.TryAcquire(DisposableName(), TimeSpan.Zero));
    }

    [Fact]
    public void ABlankNameIsRefused() => Assert.Throws<ArgumentException>(() => SingleInstanceLock.TryAcquire("", TimeSpan.Zero));
}
