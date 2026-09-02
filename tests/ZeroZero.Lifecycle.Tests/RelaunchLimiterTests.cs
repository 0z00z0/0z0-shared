using System.Globalization;
using Xunit;
using ZeroZero.Lifecycle;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>The budget is three relaunches in ten minutes, and it lives in a file so the process
/// that keeps dying cannot forget it. Each test has a folder of its own and a clock it moves.</summary>
public sealed class RelaunchLimiterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZeroZero.Lifecycle.Tests." + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();
    private readonly RecordingLogSink _log = new();

    private RelaunchLimiter Make() => new(_dir, _log, _clock, RelaunchLimiter.Limit, RelaunchLimiter.Window);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void TheBudgetIsThreeInsideTheWindow()
    {
        RelaunchLimiter limiter = Make();

        Assert.True(limiter.TryRecordRelaunch());
        Assert.True(limiter.TryRecordRelaunch());
        Assert.True(limiter.TryRecordRelaunch());
        Assert.False(limiter.TryRecordRelaunch());

        Assert.True(File.Exists(limiter.FilePath));
        Assert.Contains(_log.Infos, line => line.Contains("refused", StringComparison.Ordinal));
    }

    [Fact]
    public void ARelaunchOlderThanTheWindowNoLongerCounts()
    {
        RelaunchLimiter limiter = Make();
        for (int i = 0; i < RelaunchLimiter.Limit; i++) Assert.True(limiter.TryRecordRelaunch());
        Assert.False(limiter.TryRecordRelaunch());

        _clock.Advance(RelaunchLimiter.Window + TimeSpan.FromSeconds(1));

        Assert.True(limiter.TryRecordRelaunch());
    }

    [Fact]
    public void TheCountIsOnDiskSoTheNextProcessSeesIt()
    {
        RelaunchLimiter first = Make();
        for (int i = 0; i < RelaunchLimiter.Limit; i++) Assert.True(first.TryRecordRelaunch());

        RelaunchLimiter next = Make();

        Assert.False(next.TryRecordRelaunch());
    }

    [Fact]
    public void AFileThatCannotBeWrittenAllowsTheRelaunchAndLogsWhy()
    {
        RelaunchLimiter limiter = Make();
        // A folder where the file should be: nothing to read, and the write is refused.
        Directory.CreateDirectory(limiter.FilePath);

        Assert.True(limiter.TryRecordRelaunch());

        (string source, Exception? error) = Assert.Single(_log.Errors);
        Assert.Equal(nameof(RelaunchLimiter), source);
        Assert.NotNull(error);
    }

    [Fact]
    public void ALineThatDoesNotParseIsIgnored()
    {
        RelaunchLimiter limiter = Make();
        Directory.CreateDirectory(_dir);
        string stamp = _clock.Now.ToString("O", CultureInfo.InvariantCulture);
        File.WriteAllLines(limiter.FilePath, ["not a timestamp", "", stamp, "2026-13-45", stamp]);

        Assert.True(limiter.TryRecordRelaunch());
        Assert.False(limiter.TryRecordRelaunch());
        Assert.Empty(_log.Errors);
    }

    [Fact]
    public void TheFileKeepsOnlyWhatStillCountsInTheMachineReadableForm()
    {
        RelaunchLimiter limiter = Make();
        for (int i = 0; i < RelaunchLimiter.Limit; i++) limiter.TryRecordRelaunch();
        _clock.Advance(RelaunchLimiter.Window + TimeSpan.FromSeconds(1));

        Assert.True(limiter.TryRecordRelaunch());

        string line = Assert.Single(File.ReadAllLines(limiter.FilePath));
        Assert.Equal(_clock.Now, DateTimeOffset.ParseExact(line, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public void TheDefaultsAreThreeInTenMinutes()
    {
        Assert.Equal(3, RelaunchLimiter.Limit);
        Assert.Equal(TimeSpan.FromMinutes(10), RelaunchLimiter.Window);
    }
}
