using Xunit;

namespace ZeroZero.Startup.Tests;

/// <summary>The record of a deliberate exit, against the real file system under a folder of the
/// test's own.</summary>
public sealed class WatchdogHoldTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ZeroZero.Startup.Tests.Hold." + Guid.NewGuid().ToString("N"));
    private readonly RecordingLogSink _log = new();

    private WatchdogHold Hold(string? name = null) =>
        new(Path.Combine(_folder, name ?? "watchdog-hold.marker"), _log);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void NothingIsHeldUntilTheApplicationSaysSo()
    {
        Assert.False(Hold().IsHeld);
    }

    [Fact]
    public void ADeliberateExitIsRecordedAndReadBack()
    {
        WatchdogHold hold = Hold();

        hold.Hold();

        Assert.True(hold.IsHeld);
        // A second reader, as the relaunched process would be: the record has to survive the
        // process that wrote it.
        Assert.True(Hold().IsHeld);
    }

    [Fact]
    public void AStartTheUserAskedForArmsTheBackstopAgain()
    {
        WatchdogHold hold = Hold();
        hold.Hold();

        hold.Release();

        Assert.False(hold.IsHeld);
    }

    [Fact]
    public void ReleasingWhenNothingIsHeldIsNotAFailure()
    {
        Hold().Release();

        Assert.Empty(_log.Errors);
    }

    [Fact]
    public void AFolderThatDoesNotExistYetIsCreated()
    {
        // The first exit can come before anything else has written to the data folder.
        WatchdogHold hold = Hold(Path.Combine("not-yet", "watchdog-hold.marker"));

        hold.Hold();

        Assert.True(hold.IsHeld);
        Assert.Empty(_log.Errors);
    }

    [Fact]
    public void APathThatCannotBeWrittenCostsTheRecordAndNotTheExit()
    {
        // A directory where the marker file should be: the write fails, and the exit carries on.
        string path = Path.Combine(_folder, "occupied.marker");
        Directory.CreateDirectory(path);
        var hold = new WatchdogHold(path, _log);

        hold.Hold();

        Assert.NotEmpty(_log.Errors);
    }

    [Fact]
    public void APathIsRequired()
    {
        Assert.Throws<ArgumentException>(() => new WatchdogHold("  "));
        Assert.Throws<ArgumentNullException>(() => new WatchdogHold(null!));
    }
}
