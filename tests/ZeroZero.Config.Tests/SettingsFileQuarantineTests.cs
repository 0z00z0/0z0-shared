using Xunit;

namespace ZeroZero.Config.Tests;

/// <summary>A file that cannot be parsed is preserved before defaults take over.</summary>
public class SettingsFileQuarantineTests : SettingsFileTestBase
{
    private const string Malformed = "{ \"Label\": \"desk\", \"Retries\": ";

    [Fact]
    public void MalformedFile_IsCopiedAsideWithItsContentIntact()
    {
        File.WriteAllText(FilePath, Malformed);

        var file = Create();

        var copy = Assert.Single(QuarantineCopies());
        Assert.Equal(Malformed, File.ReadAllText(copy));
        Assert.Equal(copy, file.LastQuarantinePath);
    }

    [Fact]
    public void MalformedFile_IsReplacedByDefaults()
    {
        File.WriteAllText(FilePath, Malformed);

        var file = Create();

        Assert.Equal(3, file.Read().Retries);
        Assert.Equal(string.Empty, file.Read().Label);
        Assert.Equal(3, OnDisk().Retries);
    }

    [Fact]
    public void ValidJsonOfTheWrongShape_IsQuarantinedToo()
    {
        File.WriteAllText(FilePath, "[1, 2, 3]");

        var file = Create();

        Assert.Single(QuarantineCopies());
        Assert.Equal(3, file.Read().Retries);
    }

    [Fact]
    public void Quarantine_KeepsNoMoreCopiesThanThePolicyAllows()
    {
        var policy = new SettingsFileQuarantine(Keep: 2);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            File.WriteAllText(FilePath, Malformed);
            Create(policy);
        }

        Assert.Equal(2, QuarantineCopies().Length);
        Assert.All(QuarantineCopies(), copy => Assert.Equal(Malformed, File.ReadAllText(copy)));
    }

    [Fact]
    public void Quarantine_Off_ReplacesTheFileWithoutKeepingACopy()
    {
        File.WriteAllText(FilePath, Malformed);

        var file = Create(SettingsFileQuarantine.Off);

        Assert.Empty(QuarantineCopies());
        Assert.Null(file.LastQuarantinePath);
        Assert.Equal(3, OnDisk().Retries);
    }

    [Fact]
    public void Quarantine_HonoursItsOwnDirectory()
    {
        var aside = Path.Combine(Root, "quarantine");
        File.WriteAllText(FilePath, Malformed);

        var file = Create(new SettingsFileQuarantine(Keep: 3, Directory: aside));

        Assert.Empty(QuarantineCopies());
        Assert.Single(Directory.EnumerateFiles(aside));
        Assert.StartsWith(aside, file.LastQuarantinePath);
    }

    [Fact]
    public void Reload_QuarantinesAFileThatHasGoneBadSinceItWasLoaded()
    {
        var file = Create();
        file.Update(s => s.Label = "desk");

        File.WriteAllText(FilePath, Malformed);

        Assert.True(file.Reload());
        Assert.Single(QuarantineCopies());
        Assert.Equal(string.Empty, file.Read().Label);
    }
}
