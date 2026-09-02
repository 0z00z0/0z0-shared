using Xunit;

namespace ZeroZero.Config.Tests;

/// <summary>A file that cannot be read may still be intact, so nothing is written over it until a
/// read has succeeded — once, and for good.</summary>
public class SettingsFileUnreadableFileTests : SettingsFileTestBase
{
    private const string Malformed = "{ \"Label\": \"desk\", \"Retries\": ";

    /// <summary>A file holding real settings, and a store opened while something else holds it.</summary>
    private SettingsFile<SampleSettings> OpenWhileSeized()
    {
        Create().Update(s =>
        {
            s.Label = "desk";
            s.Retries = 7;
        });

        using (Seize()) return Create();
    }

    [Fact]
    public void UnreadableFile_ReadsDefaultsAndIsNotQuarantined()
    {
        var file = OpenWhileSeized();

        Assert.Equal(3, file.Read().Retries);
        Assert.Null(file.LastQuarantinePath);
        Assert.Empty(QuarantineCopies());
    }

    [Fact]
    public void Update_AfterAnUnreadableLoad_IsRefusedAndTheFileIsLeftWhole()
    {
        var file = OpenWhileSeized();
        var before = File.ReadAllText(FilePath);
        var failures = new List<SettingsSaveFailedEventArgs>();
        file.SaveFailed += (_, e) => failures.Add(e);

        var result = file.Update(s => s.Label = "cabinet");

        Assert.False(result.Saved);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.Equal(7, OnDisk().Retries);
        Assert.Single(failures);
        Assert.Equal(string.Empty, file.Read().Label);
    }

    [Fact]
    public void Save_AfterAnUnreadableLoad_IsRefused()
    {
        var file = OpenWhileSeized();

        var result = file.Save();

        Assert.False(result.Saved);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal("desk", OnDisk().Label);
    }

    [Fact]
    public void Reload_ThatSucceeds_LiftsTheRefusalForGood()
    {
        var file = OpenWhileSeized();

        Assert.True(file.Reload());
        Assert.Equal("desk", file.Read().Label);

        Assert.True(file.Update(s => s.Retries = 9).Saved);
        Assert.Equal("desk", OnDisk().Label);
        Assert.Equal(9, OnDisk().Retries);
    }

    [Fact]
    public void Reload_WhileTheFileIsSeized_KeepsTheHeldState()
    {
        var file = Create();
        file.Update(s => s.Label = "desk");
        var changes = 0;
        file.Changed += (_, _) => changes++;

        bool changed;
        using (Seize()) changed = file.Reload();

        Assert.False(changed);
        Assert.Equal(0, changes);
        Assert.Equal("desk", file.Read().Label);
    }

    [Fact]
    public void EmptyFile_IsARead_SoAWriteIsNotRefused()
    {
        File.WriteAllText(FilePath, string.Empty);
        var file = Create();

        Assert.True(file.Update(s => s.Label = "desk").Saved);
        Assert.Equal("desk", OnDisk().Label);
    }

    [Fact]
    public void OnceLoaded_AFileBrokenByHand_IsStillWrittenOver()
    {
        // The self-heal: a good configuration in memory over a file someone has damaged.
        var file = Create();
        file.Update(s => s.Label = "desk");
        File.WriteAllText(FilePath, Malformed);

        Assert.True(file.Update(s => s.Retries = 9).Saved);
        Assert.Equal("desk", OnDisk().Label);
        Assert.Equal(9, OnDisk().Retries);
    }

    [Fact]
    public void OnceLoaded_AReloadThatFindsTheFileBroken_DoesNotRefuseTheNextWrite()
    {
        // The latch is "has any read ever succeeded", not "did the last one": a reload that
        // quarantines a broken file leaves the store writable.
        var file = Create();
        file.Update(s => s.Label = "desk");
        File.WriteAllText(FilePath, Malformed);
        file.Reload();

        Assert.True(file.Update(s => s.Retries = 9).Saved);
        Assert.Equal(9, OnDisk().Retries);
    }

    [Fact]
    public void OnceLoaded_AReloadWhileTheFileIsSeized_DoesNotRefuseTheNextWrite()
    {
        // The same latch, the other failure: a lock met at reload is not a reason to stop saving.
        var file = Create();
        file.Update(s => s.Label = "desk");
        using (Seize()) file.Reload();

        Assert.True(file.Update(s => s.Retries = 9).Saved);
        Assert.Equal("desk", OnDisk().Label);
        Assert.Equal(9, OnDisk().Retries);
    }
}
