using Xunit;

namespace ZeroZero.Config.Tests;

/// <summary>What a read hands back, and what a missing or empty file stands for.</summary>
public class SettingsFileReadTests : SettingsFileTestBase
{
    [Fact]
    public void MissingFile_ReadsDefaultsAndCreatesNothing()
    {
        var settings = Create().Read();

        Assert.False(File.Exists(FilePath));
        Assert.Equal(3, settings.Retries);
        Assert.Equal(SampleMode.Automatic, settings.Mode);
    }

    [Fact]
    public void EmptyFile_ReadsDefaultsAndIsNotQuarantined()
    {
        File.WriteAllText(FilePath, string.Empty);

        var file = Create();

        Assert.Equal(3, file.Read().Retries);
        Assert.Null(file.LastQuarantinePath);
        Assert.Empty(QuarantineCopies());
    }

    [Fact]
    public void Read_HandsBackASnapshot_SoMutatingItChangesNothing()
    {
        var file = Create();

        var snapshot = file.Read();
        snapshot.Retries = 99;
        snapshot.Groups["battery"] = true;

        Assert.Equal(3, file.Read().Retries);
        Assert.Empty(file.Read().Groups);
    }

    [Fact]
    public void Read_HandsBackADistinctInstanceEachCall()
    {
        var file = Create();

        Assert.NotSame(file.Read(), file.Read());
    }

    [Fact]
    public void ExistingFile_IsReadBackByTheNextInstance()
    {
        Create().Update(s =>
        {
            s.Enabled = true;
            s.Label = "desk";
            s.Retries = 7;
        });

        var reopened = Create().Read();

        Assert.True(reopened.Enabled);
        Assert.Equal("desk", reopened.Label);
        Assert.Equal(7, reopened.Retries);
    }

    [Fact]
    public void FileName_CarryingADirectorySeparator_IsRejected()
    {
        var options = new SettingsFileOptions(Root, Path.Combine("nested", FileName));

        Assert.Throws<ArgumentException>(() => new SettingsFile<SampleSettings>(options));
    }

    [Fact]
    public void MutationThatThrows_LeavesTheStoredStateUntouched()
    {
        var file = Create();

        Assert.Throws<InvalidOperationException>(() => file.Update(s =>
        {
            s.Retries = 42;
            throw new InvalidOperationException("The mutation gave up half way.");
        }));

        Assert.Equal(3, file.Read().Retries);
        Assert.False(File.Exists(FilePath));
    }
}
