using Xunit;

namespace ZeroZero.Update.Tests;

public class UpdateSchedulerTests
{
    private static async Task WaitUntil(Func<bool> condition, TimeSpan within)
    {
        DateTime deadline = DateTime.UtcNow + within;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in time.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Start_RunsAfterTheDelayAndThenAtTheInterval()
    {
        int runs = 0;
        using var scheduler = new UpdateScheduler(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30), _ =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        });

        scheduler.Start();

        await WaitUntil(() => Volatile.Read(ref runs) >= 3, TimeSpan.FromSeconds(5));
        Assert.True(scheduler.Runs >= 3);
    }

    [Fact]
    public async Task Start_DoesNotRunBeforeTheDelay()
    {
        int runs = 0;
        using var scheduler = new UpdateScheduler(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), _ =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        });

        scheduler.Start();
        await Task.Delay(150);

        Assert.Equal(0, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task Dispose_StopsTheSchedule()
    {
        int runs = 0;
        var scheduler = new UpdateScheduler(TimeSpan.Zero, TimeSpan.FromMilliseconds(20), _ =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        });
        scheduler.Start();
        await WaitUntil(() => Volatile.Read(ref runs) >= 2, TimeSpan.FromSeconds(5));

        scheduler.Dispose();
        await Task.Delay(60);
        int afterStop = Volatile.Read(ref runs);
        await Task.Delay(200);

        Assert.Equal(afterStop, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task ACheckThatThrowsIsLoggedAndTheScheduleGoesOn()
    {
        int runs = 0;
        var log = new RecordingLogSink();
        using var scheduler = new UpdateScheduler(TimeSpan.Zero, TimeSpan.FromMilliseconds(20), _ =>
        {
            if (Interlocked.Increment(ref runs) == 1) throw new InvalidOperationException("first");
            return Task.CompletedTask;
        }, log);

        scheduler.Start();

        await WaitUntil(() => Volatile.Read(ref runs) >= 3, TimeSpan.FromSeconds(5));
        (string source, Exception? error) = Assert.Single(log.Errors);
        Assert.Equal(nameof(UpdateScheduler), source);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public async Task ChecksNeverOverlap_EvenWhenStartIsCalledTwice()
    {
        int inFlight = 0;
        int overlaps = 0;
        int runs = 0;
        using var scheduler = new UpdateScheduler(TimeSpan.Zero, TimeSpan.FromMilliseconds(1), async _ =>
        {
            if (Interlocked.Increment(ref inFlight) > 1) Interlocked.Increment(ref overlaps);
            await Task.Delay(30);
            Interlocked.Decrement(ref inFlight);
            Interlocked.Increment(ref runs);
        });

        scheduler.Start();
        scheduler.Start();

        await WaitUntil(() => Volatile.Read(ref runs) >= 4, TimeSpan.FromSeconds(5));
        Assert.Equal(0, Volatile.Read(ref overlaps));
    }

    [Fact]
    public void Construction_RefusesANegativeDelayOrAnEmptyInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UpdateScheduler(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1), _ => Task.CompletedTask));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UpdateScheduler(TimeSpan.Zero, TimeSpan.Zero, _ => Task.CompletedTask));
    }
}
