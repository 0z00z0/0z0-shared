using Xunit;
using ZeroZero.Diagnostics.Dumps;
using ZeroZero.Diagnostics.Tests;

namespace ZeroZero.Diagnostics.Dumps.Tests;

/// <summary>Old dumps on a real disk: the newest stay, the rest go, and a file that will not go is
/// reported rather than thrown.</summary>
public class DumpRetentionTests : IDisposable
{
    private readonly Scratch _scratch = new();
    private readonly RecordingSink _log = new();

    public void Dispose() => _scratch.Dispose();

    /// <summary>A dump file written at a stated age, so the order is the file's and not the test's.</summary>
    private string Dump(string executable, int pid, int ageMinutes)
    {
        string path = _scratch.File($"{executable}.{pid}.dmp");
        File.WriteAllText(path, "dump");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-ageMinutes));
        return path;
    }

    [Fact]
    public void TheNewestStayAndTheOldestGo()
    {
        string oldest = Dump("App.exe", 1, 50);
        string old = Dump("App.exe", 2, 40);
        string newer = Dump("App.exe", 3, 30);
        string newest = Dump("App.exe", 4, 20);

        int deleted = DumpRetention.Prune(_scratch.Directory, "App.exe", keep: 2, _log);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(newer));
        Assert.True(File.Exists(newest));
        Assert.Contains(_log.Entries, entry => entry.Kind == "info" && entry.Text.Contains("2 old crash dump"));
    }

    [Fact]
    public void AnotherExecutablesDumpsAreLeftAlone()
    {
        Dump("App.exe", 1, 50);
        Dump("App.exe", 2, 40);
        string other = Dump("Other.exe", 3, 60);
        string lookalike = _scratch.File("App.exe.notes.txt");
        File.WriteAllText(lookalike, "");

        DumpRetention.Prune(_scratch.Directory, "App.exe", keep: 1, _log);

        Assert.True(File.Exists(other));
        Assert.True(File.Exists(lookalike));
    }

    [Fact]
    public void KeepingZeroRemovesEveryDumpOfTheExecutable()
    {
        Dump("App.exe", 1, 5);
        Dump("App.exe", 2, 4);

        Assert.Equal(2, DumpRetention.Prune(_scratch.Directory, "App.exe", keep: 0, _log));
        Assert.Empty(Directory.GetFiles(_scratch.Directory, "App.exe.*.dmp"));
    }

    [Fact]
    public void KeepingMoreThanThereAreRemovesNothingAndSaysNothing()
    {
        Dump("App.exe", 1, 5);

        Assert.Equal(0, DumpRetention.Prune(_scratch.Directory, "App.exe", keep: 3, _log));
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public void AMissingDirectoryIsZeroRatherThanAnException() =>
        Assert.Equal(0, DumpRetention.Prune(_scratch.File("never-made"), "App.exe", keep: 1, _log));

    [Fact]
    public void ADumpThatWillNotDeleteIsReportedAndTheRestStillGo()
    {
        string held = Dump("App.exe", 1, 50);
        string free = Dump("App.exe", 2, 40);
        Dump("App.exe", 3, 30);
        using var holder = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);

        int deleted = DumpRetention.Prune(_scratch.Directory, "App.exe", keep: 1, _log);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(free));
        Assert.True(File.Exists(held));
        var failure = Assert.Single(_log.Entries, entry => entry.Kind == "error");
        Assert.Contains("App.exe.1.dmp", failure.Text);
        Assert.NotNull(failure.Exception);
    }

    [Fact]
    public void ANegativeKeepIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DumpRetention.Prune(_scratch.Directory, "App.exe", keep: -1, _log));
}
