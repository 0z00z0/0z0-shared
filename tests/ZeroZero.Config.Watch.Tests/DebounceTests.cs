using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ZeroZero.Config.Watch.Tests;

/// <summary>The quiet window on its own, driven by a clock the test moves. Nothing here sleeps, so
/// nothing here reports the machine's load as a result.</summary>
public sealed class DebounceTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void Nothing_runs_until_something_signals()
    {
        var clock = new FakeTimeProvider();
        var runs = 0;
        using var debounce = new TrailingDebounce(Quiet, clock, () => runs++);

        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, runs);
    }

    [Fact]
    public void Nothing_runs_before_the_window_closes()
    {
        var clock = new FakeTimeProvider();
        var runs = 0;
        using var debounce = new TrailingDebounce(Quiet, clock, () => runs++);

        debounce.Signal();
        clock.Advance(Quiet - TimeSpan.FromMilliseconds(1));

        Assert.Equal(0, runs);
    }

    [Fact]
    public void One_signal_runs_once_when_the_window_closes()
    {
        var clock = new FakeTimeProvider();
        var runs = 0;
        using var debounce = new TrailingDebounce(Quiet, clock, () => runs++);

        debounce.Signal();
        clock.Advance(Quiet);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void A_burst_inside_one_window_runs_once()
    {
        var clock = new FakeTimeProvider();
        var runs = 0;
        using var debounce = new TrailingDebounce(Quiet, clock, () => runs++);

        for (var signal = 0; signal < 8; signal++) debounce.Signal();
        clock.Advance(Quiet);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void Each_signal_restarts_the_window()
    {
        var clock = new FakeTimeProvider();
        var runs = 0;
        using var debounce = new TrailingDebounce(Quiet, clock, () => runs++);

        debounce.Signal();
        clock.Advance(TimeSpan.FromMilliseconds(400));
        debounce.Signal();
        clock.Advance(TimeSpan.FromMilliseconds(400));

        // 800 ms since the first signal, but only 400 ms of quiet since the last.
        Assert.Equal(0, runs);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Two_bursts_a_window_apart_run_twice()
    {
        var clock = new FakeTimeProvider();
        var runs = 0;
        using var debounce = new TrailingDebounce(Quiet, clock, () => runs++);

        debounce.Signal();
        clock.Advance(Quiet);
        debounce.Signal();
        clock.Advance(Quiet);

        Assert.Equal(2, runs);
    }

    [Fact]
    public void A_window_that_closes_twice_with_no_new_signal_runs_once()
    {
        var clock = new FakeTimeProvider();
        var runs = 0;
        using var debounce = new TrailingDebounce(Quiet, clock, () => runs++);

        debounce.Signal();
        clock.Advance(Quiet);
        clock.Advance(Quiet);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void A_signal_raised_during_a_run_starts_a_fresh_window()
    {
        var clock = new FakeTimeProvider();
        var runs = 0;
        TrailingDebounce? debounce = null;

        debounce = new TrailingDebounce(Quiet, clock, () =>
        {
            runs++;
            if (runs == 1) debounce!.Signal();
        });

        using (debounce)
        {
            debounce.Signal();
            clock.Advance(Quiet);
            Assert.Equal(1, runs);

            clock.Advance(Quiet);
            Assert.Equal(2, runs);
        }
    }

    [Fact]
    public void An_elapse_arriving_with_nothing_pending_runs_nothing()
    {
        var provider = new ManualTimerProvider();
        var runs = 0;
        using var debounce = new TrailingDebounce(Quiet, provider, () => runs++);

        debounce.Signal();
        provider.Fire();
        Assert.Equal(1, runs);

        // A one-shot timer still delivers a callback that was queued before it was re-armed, so a
        // second elapse can arrive with nothing having signalled. It must run nothing.
        provider.Fire();
        Assert.Equal(1, runs);
    }

    [Fact]
    public void A_disposed_debounce_runs_nothing_further()
    {
        var clock = new FakeTimeProvider();
        var runs = 0;
        var debounce = new TrailingDebounce(Quiet, clock, () => runs++);

        debounce.Signal();
        debounce.Dispose();
        clock.Advance(Quiet);

        Assert.Equal(0, runs);
    }

    [Fact]
    public void A_negative_window_is_refused()
    {
        var clock = new FakeTimeProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrailingDebounce(TimeSpan.FromMilliseconds(-1), clock, () => { }));
    }
}
