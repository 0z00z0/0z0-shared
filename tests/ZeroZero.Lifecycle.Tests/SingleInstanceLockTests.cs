using System.Diagnostics;
using Xunit;
using ZeroZero.Lifecycle;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>Real named mutexes under names no application uses, with the other instance played by
/// a thread of its own. The acquisition is tested through the internal seam that hands the mutex
/// back, so each test can release what it took; the public path is exercised once, in one test, in
/// an order it sets itself — the lock it takes is process-wide and stays held for the rest of the
/// run, so a second public-path test would answer differently depending on which ran first.</summary>
public class SingleInstanceLockTests
{
    private static string DisposableName() => @"Local\ZeroZero.Lifecycle.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void AFreeNameIsTakenAtOnce()
    {
        SingleInstanceOutcome outcome = SingleInstanceLock.Open(DisposableName(), TimeSpan.Zero, out Mutex? opened);
        using Mutex? taken = opened;

        Assert.Equal(SingleInstanceOutcome.TakenFree, outcome);
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
        SingleInstanceOutcome outcome = SingleInstanceLock.Open(name, TimeSpan.FromMilliseconds(300), out Mutex? opened);
        stopwatch.Stop();
        using Mutex? taken = opened;

        Assert.Equal(SingleInstanceOutcome.RefusedHeld, outcome);
        Assert.False(outcome.IsTaken());
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

        SingleInstanceOutcome outcome = SingleInstanceLock.Open(name, TimeSpan.FromSeconds(10), out Mutex? opened);
        using Mutex? taken = opened;

        Assert.Equal(SingleInstanceOutcome.TakenFree, outcome);
        Assert.NotNull(taken);
        taken.ReleaseMutex();
    }

    [Fact]
    public void AMutexItsHolderAbandonedCountsAsTakenAndSaysWhich()
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

        SingleInstanceOutcome outcome = SingleInstanceLock.Open(name, TimeSpan.Zero, out Mutex? opened);
        using Mutex? taken = opened;

        // The distinction the result exists for: this instance runs, and the one before it died. A
        // free name reports TakenFree, and the two must never be the same answer.
        Assert.Equal(SingleInstanceOutcome.TakenAbandoned, outcome);
        Assert.NotEqual(SingleInstanceOutcome.TakenFree, outcome);
        Assert.True(outcome.IsTaken());
        Assert.NotNull(taken);
        taken.ReleaseMutex();
    }

    [Fact]
    public void ANameThisProcessMayNotOpenIsRefusedAsDeniedRatherThanHeld()
    {
        string name = DisposableName();
        using var denied = new DeniedMutex(name);

        SingleInstanceOutcome outcome = SingleInstanceLock.Open(name, TimeSpan.Zero, out Mutex? opened);
        using Mutex? taken = opened;

        // A refusal an application can act on, rather than an access error thrown at a process that
        // has no logger yet. Held and denied are different facts and must not collapse.
        Assert.Equal(SingleInstanceOutcome.RefusedDenied, outcome);
        Assert.NotEqual(SingleInstanceOutcome.RefusedHeld, outcome);
        Assert.False(outcome.IsTaken());
        Assert.Null(taken);
    }

    [Fact]
    public void ANameNamingAnotherKindOfObjectThrowsRatherThanReadingAsARefusal()
    {
        string name = DisposableName();
        using var semaphore = new Semaphore(1, 1, name);

        // A name that cannot be a mutex at all is the application's own error. Answered as a refusal
        // it would look like an ordinary second instance and never be found.
        Assert.Throws<WaitHandleCannotBeOpenedException>(() => SingleInstanceLock.Open(name, TimeSpan.Zero, out _));
    }

    [Fact]
    public void OnlyTheTwoTakenOutcomesCountAsTaken()
    {
        Assert.True(SingleInstanceOutcome.TakenFree.IsTaken());
        Assert.True(SingleInstanceOutcome.TakenAbandoned.IsTaken());
        Assert.False(SingleInstanceOutcome.RefusedHeld.IsTaken());
        Assert.False(SingleInstanceOutcome.RefusedDenied.IsTaken());
    }

    [Fact]
    public void ThePublicPathRefusesTwiceAndThenHoldsOneLockForTheProcessLifetime()
    {
        Assert.False(SingleInstanceLock.IsHeld);

        // A name this process may not open: refused, and nothing is held afterwards.
        string forbidden = DisposableName();
        using (new DeniedMutex(forbidden))
        {
            Assert.Equal(SingleInstanceOutcome.RefusedDenied, SingleInstanceLock.Acquire(forbidden, TimeSpan.Zero));
            Assert.False(SingleInstanceLock.TryAcquire(forbidden, TimeSpan.Zero));
            Assert.False(SingleInstanceLock.IsHeld);
        }

        // A name another instance holds: refused, and nothing is held afterwards.
        string busy = DisposableName();
        using (var other = new MutexHolder(busy))
        {
            Assert.True(other.Acquired);
            Assert.Equal(SingleInstanceOutcome.RefusedHeld, SingleInstanceLock.Acquire(busy, TimeSpan.Zero));
            Assert.False(SingleInstanceLock.TryAcquire(busy, TimeSpan.Zero));
            Assert.False(SingleInstanceLock.IsHeld);
        }

        string name = DisposableName();
        Assert.Equal(SingleInstanceOutcome.TakenFree, SingleInstanceLock.Acquire(name, TimeSpan.Zero));
        Assert.True(SingleInstanceLock.IsHeld);

        // Another instance, played by a thread of its own, finds the name taken with no wait at all.
        // Not a pool thread: ownership belongs to the thread that took the lock, and an awaited
        // Task.Run can land on that very thread once the test yields it, where a second wait succeeds.
        using (var other = new MutexHolder(name))
            Assert.False(other.Acquired);

        // The same name again is the lock already held, and reports the outcome it was taken with;
        // another name is a second product.
        Assert.Equal(SingleInstanceOutcome.TakenFree, SingleInstanceLock.Acquire(name, TimeSpan.Zero));
        Assert.True(SingleInstanceLock.TryAcquire(name, TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => SingleInstanceLock.Acquire(DisposableName(), TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => SingleInstanceLock.TryAcquire(DisposableName(), TimeSpan.Zero));
    }

    [Fact]
    public void ABlankNameIsRefused()
    {
        Assert.Throws<ArgumentException>(() => SingleInstanceLock.TryAcquire("", TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => SingleInstanceLock.Acquire("", TimeSpan.Zero));
    }
}
