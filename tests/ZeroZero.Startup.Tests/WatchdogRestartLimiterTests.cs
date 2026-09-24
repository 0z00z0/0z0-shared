using Xunit;

namespace ZeroZero.Startup.Tests;

/// <summary>The bound on the probe's own restarts: three probe-started runs inside fifteen minutes,
/// none of which stayed up two minutes. Each test has a folder of its own and a clock it moves, so
/// fifteen minutes cost nothing.</summary>
public sealed class WatchdogRestartLimiterTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZeroZero.Startup.Tests.Restarts." + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();
    private readonly RecordingLogSink _log = new();

    private WatchdogRestartLimiter Make() =>
        new(_dir, Interval, _log, _clock, WatchdogRestartLimiter.Limit, WatchdogRestartLimiter.Window, WatchdogRestartLimiter.StaysUp);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void TheThirdProbeStartInsideTheWindowEndsTheProbing()
    {
        WatchdogRestartLimiter limiter = Make();

        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));
        _clock.Advance(Interval);
        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));
        _clock.Advance(Interval);

        Assert.False(limiter.RecordStart(WatchdogStartCause.Probe));
        Assert.True(File.Exists(limiter.FilePath));
        Assert.Single(_log.Infos, line => line.Contains("probing stopped", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunThatStayedUpStartsTheCountAgain()
    {
        // The gap carries the run's length: longer than the interval plus the bar means the
        // application was up past it, so the two starts before this one were not a loop.
        WatchdogRestartLimiter limiter = Make();
        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));
        _clock.Advance(Interval);
        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));

        _clock.Advance(Interval + WatchdogRestartLimiter.StaysUp + TimeSpan.FromSeconds(1));

        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));
        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));
        Assert.False(limiter.RecordStart(WatchdogStartCause.Probe));
    }

    [Fact]
    public void AStartOlderThanTheWindowNoLongerCounts()
    {
        WatchdogRestartLimiter limiter = Make();
        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));

        _clock.Advance(WatchdogRestartLimiter.Window + TimeSpan.FromMinutes(1));

        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));
        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));
        Assert.False(limiter.RecordStart(WatchdogStartCause.Probe));
    }

    [Fact]
    public void AStartThePersonAskedForClearsTheCount()
    {
        WatchdogRestartLimiter limiter = Make();
        limiter.RecordStart(WatchdogStartCause.Probe);
        limiter.RecordStart(WatchdogStartCause.Probe);

        Assert.True(limiter.RecordStart(WatchdogStartCause.Person));

        Assert.False(File.Exists(limiter.FilePath));
        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));
        Assert.True(limiter.RecordStart(WatchdogStartCause.Probe));
        Assert.False(limiter.RecordStart(WatchdogStartCause.Probe));
    }

    [Fact]
    public void AStateDirectoryIsRequired()
    {
        Assert.Throws<ArgumentException>(() => new WatchdogRestartLimiter("  ", Interval));
        Assert.Throws<ArgumentNullException>(() => new WatchdogRestartLimiter(null!, Interval));
    }
}
