using Xunit;

namespace ZeroZero.Update.Tests;

public class DownloadDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ZeroZero.Update.Tests-dirs-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingLogSink _log = new();

    public DownloadDirectoryTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Create_MakesAFreshDirectoryNamedByThePrefixAndAnIdentifier()
    {
        string first = DownloadDirectory.Create("Product-update", _root);
        string second = DownloadDirectory.Create("Product-update", _root);

        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
        Assert.NotEqual(first, second);
        Assert.Equal(_root, Path.GetDirectoryName(first));
        string name = Path.GetFileName(first);
        Assert.StartsWith("Product-update-", name);
        Assert.Equal(32, name.Length - "Product-update-".Length);
        Assert.True(name["Product-update-".Length..].All(char.IsAsciiHexDigit));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData(@"has\slash")]
    [InlineData("has*star")]
    public void Create_RefusesAPrefixThatIsNotAPlainName(string prefix)
    {
        Assert.Throws<ArgumentException>(() => DownloadDirectory.Create(prefix, _root));
    }

    [Fact]
    public void Sweep_RemovesOldDirectoriesOfTheShapeAndNothingElse()
    {
        string stale = DownloadDirectory.Create("Product-update", _root);
        File.WriteAllText(Path.Combine(stale, "installer.exe"), "old");
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        string current = DownloadDirectory.Create("Product-update", _root);
        string fresh = DownloadDirectory.Create("Product-update", _root);
        string otherPrefix = DownloadDirectory.Create("Other-update", _root);
        Directory.SetLastWriteTimeUtc(otherPrefix, DateTime.UtcNow.AddDays(-2));
        string lookalike = Path.Combine(_root, "Product-update-notahexidentifier");
        Directory.CreateDirectory(lookalike);
        Directory.SetLastWriteTimeUtc(lookalike, DateTime.UtcNow.AddDays(-2));

        int removed = DownloadDirectory.Sweep("Product-update", TimeSpan.FromHours(1), _log, _root, except: current);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(current));
        Assert.True(Directory.Exists(fresh));
        Assert.True(Directory.Exists(otherPrefix));
        Assert.True(Directory.Exists(lookalike));
        Assert.Empty(_log.Errors);
    }

    [Fact]
    public void Sweep_KeepsTheCurrentDirectoryWhateverItsAge()
    {
        string current = DownloadDirectory.Create("Product-update", _root);
        Directory.SetLastWriteTimeUtc(current, DateTime.UtcNow.AddDays(-2));

        int removed = DownloadDirectory.Sweep("Product-update", TimeSpan.Zero, _log, _root, except: current);

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(current));
    }

    [Fact]
    public void Sweep_LogsADirectoryItCannotRemoveAndGoesOn()
    {
        string locked = DownloadDirectory.Create("Product-update", _root);
        string stale = DownloadDirectory.Create("Product-update", _root);
        Directory.SetLastWriteTimeUtc(locked, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));

        using (new FileStream(Path.Combine(locked, "installer.exe"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            int removed = DownloadDirectory.Sweep("Product-update", TimeSpan.Zero, _log, _root);

            Assert.Equal(1, removed);
            Assert.False(Directory.Exists(stale));
            Assert.Single(_log.Errors);
        }
    }

    [Fact]
    public void Sweep_OfAMissingRootRemovesNothing()
    {
        Assert.Equal(0, DownloadDirectory.Sweep("Product-update", TimeSpan.Zero, _log, Path.Combine(_root, "absent")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temporary folder's own housekeeping.
        }
    }
}
