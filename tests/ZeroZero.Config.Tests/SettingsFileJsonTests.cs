using Xunit;

namespace ZeroZero.Config.Tests;

/// <summary>How the file represents what it stores, and what an older or hand-edited file may look
/// like without being rejected.</summary>
public class SettingsFileJsonTests : SettingsFileTestBase
{
    [Fact]
    public void Enums_ArePersistedAsTheirDeclaredMemberName()
    {
        Create().Update(s => s.Mode = SampleMode.Cold);

        var text = File.ReadAllText(FilePath);

        Assert.Contains("\"Cold\"", text);
        Assert.DoesNotContain("\"Mode\": 2", text);
    }

    [Fact]
    public void Enums_SurviveTheirMembersBeingRenumbered()
    {
        // A name in the file resolves by name, so inserting or reordering members later cannot
        // silently turn one setting into another.
        File.WriteAllText(FilePath, "{ \"Mode\": \"Cold\" }");

        Assert.Equal(SampleMode.Cold, Create().Read().Mode);
    }

    [Fact]
    public void Enums_StillReadANumberLeftByAnOlderWriter()
    {
        File.WriteAllText(FilePath, "{ \"Mode\": 2 }");

        Assert.Equal(SampleMode.Cold, Create().Read().Mode);
    }

    [Fact]
    public void Enums_ReadCaseInsensitively()
    {
        File.WriteAllText(FilePath, "{ \"Mode\": \"cold\" }");

        Assert.Equal(SampleMode.Cold, Create().Read().Mode);
    }

    [Fact]
    public void Enums_AnUnknownMemberNameCostsEveryOtherSettingInTheFile()
    {
        // The blast radius is the file, not the field: a name no member answers to fails the whole
        // deserialisation, so the file is quarantined and every unrelated setting beside it reverts
        // to its default. Retiring an enum member therefore drops the user's other settings on
        // upgrade — the copy left aside is the only thing standing between that and outright loss.
        File.WriteAllText(FilePath, "{ \"Label\": \"kept-by-the-user\", \"Mode\": \"Tepid\" }");

        var file = Create();

        Assert.Equal(string.Empty, file.Read().Label);
        Assert.Equal(SampleMode.Automatic, file.Read().Mode);
        Assert.Single(QuarantineCopies());
        Assert.Contains("kept-by-the-user", File.ReadAllText(QuarantineCopies()[0]));
    }

    [Fact]
    public void PropertyNames_ReadCaseInsensitively()
    {
        File.WriteAllText(FilePath, "{ \"label\": \"desk\", \"RETRIES\": 9 }");

        var settings = Create().Read();

        Assert.Equal("desk", settings.Label);
        Assert.Equal(9, settings.Retries);
    }

    [Fact]
    public void HandEdits_WithCommentsAndTrailingCommasAreAccepted()
    {
        File.WriteAllText(FilePath, "{ /* set by hand */ \"Label\": \"desk\", }");

        Assert.Equal("desk", Create().Read().Label);
    }

    [Fact]
    public void UnknownProperties_AreIgnoredRatherThanQuarantined()
    {
        File.WriteAllText(FilePath, "{ \"Label\": \"desk\", \"Retired\": true }");

        var file = Create();

        Assert.Equal("desk", file.Read().Label);
        Assert.Empty(QuarantineCopies());
    }

    [Fact]
    public void TheFileIsIndentedSoItCanBeReadAndEdited()
    {
        Create().Update(s => s.Label = "desk");

        var text = File.ReadAllText(FilePath);

        Assert.Contains('\n', text);
        Assert.Contains("  \"Label\"", text);
    }

    [Fact]
    public void DefaultSerialiser_IsLockedAgainstMutation()
        => Assert.Throws<InvalidOperationException>(
            () => SettingsFileOptions.DefaultSerialiser.WriteIndented = false);
}
