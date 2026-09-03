using System.Diagnostics;
using System.Text;
using Xunit;

namespace ZeroZero.Config.Tests;

/// <summary>The write that lands whole or not at all, on its own.</summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _root;

    public AtomicFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ZeroZero.Config.Tests.Atomic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private string FilePath => Path.Combine(_root, "payload.bin");

    [Fact]
    public void AWriteLandsAndLeavesNoTemporarySibling()
    {
        Assert.Null(AtomicFile.Write(FilePath, "hello"u8));

        Assert.Equal("hello"u8.ToArray(), File.ReadAllBytes(FilePath));
        Assert.False(File.Exists(FilePath + AtomicFile.TempSuffix));
    }

    [Fact]
    public void AWriteCreatesTheDirectoryItNeeds()
    {
        var nested = Path.Combine(_root, "one", "two", "payload.bin");

        Assert.Null(AtomicFile.Write(nested, "hello"u8));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void TextIsWrittenAsUtf8WithNoByteOrderMark()
    {
        Assert.Null(AtomicFile.WriteText(FilePath, "målepunkt"));

        var bytes = File.ReadAllBytes(FilePath);
        Assert.NotEqual(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal("målepunkt", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void AFailedWriteReturnsTheReasonAndLeavesTheFileWhole()
    {
        Assert.Null(AtomicFile.Write(FilePath, "first"u8));

        using var seized = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var error = AtomicFile.Write(FilePath, "second"u8);

        Assert.NotNull(error);
        Assert.True(AtomicFile.IsFileFailure(error));
        Assert.False(File.Exists(FilePath + AtomicFile.TempSuffix));
    }

    [Fact]
    public void AReplaceTheSystemRefusesIsRetriedBeforeItIsGivenUpOn()
    {
        Assert.Null(AtomicFile.Write(FilePath, "first"u8));

        // The file is held for the whole call, so the elapsed time is the retries and nothing else:
        // a single attempt returns at once, and five attempts twenty milliseconds apart cannot.
        using var seized = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var clock = Stopwatch.StartNew();
        Assert.NotNull(AtomicFile.Write(FilePath, "second"u8));
        clock.Stop();

        Assert.True(
            clock.ElapsedMilliseconds >= 60,
            $"A refused replace returned after {clock.ElapsedMilliseconds} ms, so it was not retried.");
    }

    [Fact]
    public void DeletingWhatIsNotThereReportsNothing()
    {
        AtomicFile.TryDelete(Path.Combine(_root, "never-existed.bin"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary folder is the operating system's problem, not the test's.
        }

        GC.SuppressFinalize(this);
    }
}
