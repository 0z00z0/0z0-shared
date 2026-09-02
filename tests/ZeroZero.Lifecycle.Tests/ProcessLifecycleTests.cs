using Xunit;
using ZeroZero.Lifecycle;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>The exit decision, in this process, against a limiter file in a folder of the test's
/// own. What the decision does to a real process is in <see cref="ProcessLifecycleProcessTests"/>.</summary>
public sealed class ProcessLifecycleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZeroZero.Lifecycle.Tests." + Guid.NewGuid().ToString("N"));
    private readonly RecordingLogSink _log = new();

    private ProcessLifecycle Make(params string[] args) =>
        new(new ProcessLifecycleOptions { DataDirectory = _dir, ExecutablePath = Path.Combine(_dir, "app.exe"), Log = _log }, args);

    private string LimiterFile => Path.Combine(_dir, RelaunchLimiter.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void AnExitNobodyAskedForIsRelaunchedAndCounted()
    {
        Assert.Equal(RelaunchDecision.Relaunch, Make().DecideOnExit());

        Assert.Single(File.ReadAllLines(LimiterFile));
    }

    [Fact]
    public void ADeliberateExitIsNeitherRelaunchedNorCounted()
    {
        ProcessLifecycle lifecycle = Make();
        lifecycle.MarkDeliberateExit();

        Assert.Equal(RelaunchDecision.DeliberateExit, lifecycle.DecideOnExit());

        Assert.False(File.Exists(LimiterFile));
    }

    [Fact]
    public void AnExitBecauseTheSessionIsEndingIsNeitherRelaunchedNorCounted()
    {
        ProcessLifecycle lifecycle = Make();
        lifecycle.NoteSessionEnding();

        Assert.Equal(RelaunchDecision.SessionEnding, lifecycle.DecideOnExit());

        Assert.False(File.Exists(LimiterFile));
    }

    [Fact]
    public void TheDeliberateMarkOutranksTheSessionEnding()
    {
        ProcessLifecycle lifecycle = Make();
        lifecycle.NoteSessionEnding();
        lifecycle.MarkDeliberateExit();

        Assert.Equal(RelaunchDecision.DeliberateExit, lifecycle.DecideOnExit());
    }

    [Fact]
    public void TheFourthUnmarkedExitInTheWindowIsNotRelaunched()
    {
        for (int i = 0; i < RelaunchLimiter.Limit; i++)
            Assert.Equal(RelaunchDecision.Relaunch, Make().DecideOnExit());

        Assert.Equal(RelaunchDecision.LimitReached, Make().DecideOnExit());
    }

    [Fact]
    public void ARelaunchIsRecognisedFromItsArgument()
    {
        Assert.True(Make(Relaunch.Argument).IsRelaunch);
        Assert.False(Make().IsRelaunch);
        Assert.False(Make("--startup").IsRelaunch);
    }

    [Fact]
    public void AMissingDataFolderIsRefused() =>
        Assert.Throws<ArgumentException>(() => new ProcessLifecycle(new ProcessLifecycleOptions { DataDirectory = "" }, []));
}
