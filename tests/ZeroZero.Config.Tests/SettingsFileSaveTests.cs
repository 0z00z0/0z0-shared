using Xunit;

namespace ZeroZero.Config.Tests;

/// <summary>Atomic replacement, and what happens when the file will not take the write.</summary>
public class SettingsFileSaveTests : SettingsFileTestBase
{
    /// <summary>Windows refuses a move over a seized file with either exception, depending on the
    /// handle that holds it.</summary>
    private static bool IsRefusal(Exception? error) => error is IOException or UnauthorizedAccessException;

    [Fact]
    public void Update_ReplacesTheFileAndLeavesNoTemporarySibling()
    {
        var file = Create();

        var result = file.Update(s => s.Label = "desk");

        Assert.True(result.Saved);
        Assert.Null(result.Error);
        Assert.True(File.Exists(FilePath));
        Assert.False(File.Exists(TempPath));
        Assert.Equal("desk", OnDisk().Label);
    }

    [Fact]
    public void Save_MaterialisesDefaultsForAFileThatWasNeverWritten()
    {
        var file = Create();

        Assert.True(file.Save().Saved);
        Assert.Equal(3, OnDisk().Retries);
    }

    [Fact]
    public void Update_ThatChangesNothing_WritesNothing()
    {
        var file = Create();
        file.Update(s => s.Retries = 5);

        // Anything the second update writes would put the file back.
        File.Delete(FilePath);
        var result = file.Update(s => s.Retries = 5);

        Assert.True(result.Saved);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void StaleTemporaryFile_FromAnInterruptedWrite_DoesNotBlockTheNextSave()
    {
        File.WriteAllText(TempPath, "half a payload, no closing brace");
        var file = Create();

        Assert.True(file.Update(s => s.Label = "desk").Saved);
        Assert.False(File.Exists(TempPath));
        Assert.Equal("desk", OnDisk().Label);
    }

    [Fact]
    public void FailedWrite_IsReportedToTheCaller()
    {
        var file = Create();
        file.Update(s => s.Label = "desk");

        SettingsSaveResult result;
        using (Seize())
        {
            result = file.Update(s => s.Label = "cabinet");
        }

        Assert.False(result.Saved);
        Assert.True(IsRefusal(result.Error), $"Expected the file-system refusal, got {result.Error}.");
    }

    [Fact]
    public void FailedWrite_LeavesTheExistingFileWhole()
    {
        var file = Create();
        file.Update(s => s.Label = "desk");
        var before = File.ReadAllText(FilePath);

        using (Seize())
        {
            file.Update(s => s.Label = "cabinet");
        }

        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.Equal("desk", OnDisk().Label);
    }

    [Fact]
    public void FailedWrite_RollsBackTheStoredStateAndRemovesTheTemporarySibling()
    {
        var file = Create();
        file.Update(s => s.Label = "desk");

        using (Seize())
        {
            file.Update(s => s.Label = "cabinet");
        }

        Assert.Equal("desk", file.Read().Label);
        Assert.False(File.Exists(TempPath));
    }

    [Fact]
    public void FailedWrite_RaisesSaveFailedRatherThanChanged()
    {
        var file = Create();
        file.Update(s => s.Label = "desk");

        var failures = new List<SettingsSaveFailedEventArgs>();
        var changes = 0;
        file.SaveFailed += (_, e) => failures.Add(e);
        file.Changed += (_, _) => changes++;

        using (Seize())
        {
            file.Update(s => s.Label = "cabinet");
        }

        var failure = Assert.Single(failures);
        Assert.Equal(FilePath, failure.FilePath);
        Assert.True(IsRefusal(failure.Error), $"Expected the file-system refusal, got {failure.Error}.");
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ConcurrentUpdates_AreSerialisedAndNoneIsLost()
    {
        const int writers = 50;
        var file = Create();

        Parallel.For(0, writers, _ => file.Update(s => s.Retries++));

        Assert.Equal(3 + writers, file.Read().Retries);
        Assert.Equal(3 + writers, OnDisk().Retries);
    }

    [Fact]
    public void ConcurrentUpdates_ToDifferentFieldsAllSurvive()
    {
        var file = Create();

        Parallel.For(0, 20, i => file.Update(s => s.Groups[$"group{i}"] = true));

        Assert.Equal(20, file.Read().Groups.Count);
        Assert.Equal(20, OnDisk().Groups.Count);
    }
}
