using System.Text.RegularExpressions;
using Xunit;
using ZeroZero.Diagnostics;

namespace ZeroZero.Diagnostics.Tests;

/// <summary>The file a crash reaches last. What is asserted is the entry a reader finds, and that the
/// failures a crashing process meets — a locked file, a path that is a file — come back as false and
/// never as an exception.</summary>
public class CrashLineAppenderTests : IDisposable
{
    private readonly Scratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void AnEntryCarriesTheStampTheSourceTheTypeTheMessageAndTheStack()
    {
        var appender = new CrashLineAppender(_scratch.File("crash.log"));

        bool written = appender.Append("AppDomain.UnhandledException", Thrown("the pump stopped"));

        Assert.True(written);
        string[] lines = File.ReadAllLines(appender.FilePath);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}  AppDomain\.UnhandledException  System\.InvalidOperationException: the pump stopped$"), lines[0]);
        Assert.Contains(lines.Skip(1), line => line.Contains(" at ") && line.Contains(nameof(Thrown)));
    }

    [Fact]
    public void AMissingExceptionIsSaidRatherThanInvented()
    {
        var appender = new CrashLineAppender(_scratch.File("crash.log"));

        appender.Append("source", null);

        Assert.EndsWith("  source  (no exception)", File.ReadAllText(appender.FilePath).TrimEnd());
    }

    [Fact]
    public void ThePathIsMadeFullAndItsDirectoryCreatedOnFirstWrite()
    {
        string nested = Path.Combine(_scratch.Directory, "logs", "deeper", "crash.log");
        var appender = new CrashLineAppender(nested);

        Assert.False(Directory.Exists(Path.GetDirectoryName(nested)));
        Assert.True(appender.Append("first"));
        Assert.True(File.Exists(nested));
        Assert.Equal(nested, appender.FilePath);
    }

    [Fact]
    public void EntriesAccumulateAcrossAppenders()
    {
        string path = _scratch.File("crash.log");
        new CrashLineAppender(path).Append("one");
        new CrashLineAppender(path).Append("two");

        string[] lines = File.ReadAllLines(path);

        Assert.Equal(2, lines.Length);
        Assert.EndsWith("  one", lines[0]);
        Assert.EndsWith("  two", lines[1]);
    }

    [Fact]
    public void ALockedFileIsAnsweredWithFalseAndNoException()
    {
        var appender = new CrashLineAppender(_scratch.File("crash.log"));
        using var lockHolder = new FileStream(appender.FilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        bool written = false;
        var exception = Record.Exception(() => written = appender.Append("source", Thrown("locked")));

        Assert.Null(exception);
        Assert.False(written);
    }

    [Fact]
    public void AReaderHoldingTheFileOpenDoesNotBlockTheEntry()
    {
        var appender = new CrashLineAppender(_scratch.File("crash.log"));
        appender.Append("before the reader");
        using var reader = new FileStream(appender.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        Assert.True(appender.Append("while the reader holds it"));
        Assert.Equal(2, File.ReadAllLines(appender.FilePath).Length);
    }

    [Fact]
    public void APathWhoseDirectoryIsAFileIsAnsweredWithFalseAndNoException()
    {
        string blocker = _scratch.File("blocker");
        File.WriteAllText(blocker, "");
        var appender = new CrashLineAppender(Path.Combine(blocker, "crash.log"));

        bool written = true;
        var exception = Record.Exception(() => written = appender.Append("source", Thrown("blocked")));

        Assert.Null(exception);
        Assert.False(written);
    }

    [Fact]
    public void AsASinkInfoAndErrorLandInTheSameFile()
    {
        var appender = new CrashLineAppender(_scratch.File("crash.log"));

        appender.Info("started");
        appender.Error("Application.UnhandledException", Thrown("boom"));

        string text = File.ReadAllText(appender.FilePath);
        Assert.Contains("  started", text);
        Assert.Contains("  Application.UnhandledException  System.InvalidOperationException: boom", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructionRefusesNoPath(string? path) =>
        Assert.ThrowsAny<ArgumentException>(() => new CrashLineAppender(path!));
}
