using System.Text.Json;
using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>What happens when a name in the file and a name in this build differ only in case.</summary>
/// <remarks>
/// <para>A reader takes the last of two keys that differ only in case, so a build that writes its own
/// spelling beside the file's leaves the person's value in the file with nothing reading it. The
/// consuming application's reader is case-sensitive, so a type that spells a member differently reads
/// nothing at all and reports no failure — which is the shape these tests exist to make loud.</para>
/// <para>The default serialiser matches case-insensitively, and that path is proven here too: it
/// finds the file's own spelling and replaces the value behind it, so no twin is ever created.</para>
/// </remarks>
public sealed class KeyCaseTests : SectionedTestBase
{
    // The reader shape the consuming application uses: the declared spelling and nothing else.
    private static JsonSerializerOptions CaseSensitive()
    {
        var options = SettingsFileOptions.CreateSerialiser();
        options.PropertyNameCaseInsensitive = false;
        return options;
    }

    [Fact]
    public void A_section_the_file_spells_differently_reads_as_defaults_and_names_the_files_own_spelling()
    {
        Given("""
            {
              "version": 1,
              "General": { "StartMinimised": true, "Label": "kept" }
            }
            """);

        var general = Create().Section<GeneralSection>("general");

        Assert.False(general.IsPresent);
        Assert.Equal(string.Empty, general.Read().Label);
        Assert.Equal("General", general.ConflictingKey);
    }

    [Fact]
    public void A_section_the_file_spells_the_same_way_names_no_conflict()
    {
        Given("""
            {
              "version": 1,
              "general": { "Label": "kept" }
            }
            """);

        Assert.Null(Create().Section<GeneralSection>("general").ConflictingKey);
    }

    [Fact]
    public void A_write_that_would_add_a_section_differing_only_in_case_is_refused_and_nothing_moves()
    {
        Given("""
            {
              "version": 1,
              "General": { "StartMinimised": true, "Label": "kept" }
            }
            """);
        var before = OnDiskBytes();

        var result = Create().Section<GeneralSection>("general").Update(g => g.Retries = 9);

        Assert.False(result.Saved);
        var conflict = Assert.IsType<SettingsKeyCaseConflictException>(result.Error);
        Assert.Equal("general", conflict.Wanted);
        Assert.Equal("General", conflict.Found);
        Assert.Equal(before, OnDiskBytes());
    }

    [Fact]
    public void A_refused_case_clash_is_announced()
    {
        Given("""
            {
              "version": 1,
              "General": { "Label": "kept" }
            }
            """);

        var store = Create();
        SettingsSaveFailedEventArgs? announced = null;
        store.SaveFailed += (_, args) => announced = args;

        store.Section<GeneralSection>("general").Update(g => g.Retries = 9);

        Assert.NotNull(announced);
        Assert.IsType<SettingsKeyCaseConflictException>(announced.Error);
        Assert.Equal(FilePath, announced.FilePath);
    }

    [Fact]
    public void A_member_the_type_spells_differently_is_refused_rather_than_written_beside_it()
    {
        Given("""
            {
              "version": 1,
              "general": { "startminimised": true, "label": "kept" }
            }
            """);
        var before = OnDiskBytes();

        var store = new SectionedSettingsFile(Options() with { Serialiser = CaseSensitive() });
        var result = store.Section<GeneralSection>("general").Update(g => g.Retries = 9);

        Assert.False(result.Saved);
        var conflict = Assert.IsType<SettingsKeyCaseConflictException>(result.Error);
        Assert.Equal("StartMinimised", conflict.Wanted);
        Assert.Equal("startminimised", conflict.Found);

        // The values the person set are still the only ones in the file, and still the ones a reader
        // takes; without the refusal they would sit above a pair of defaults.
        Assert.Equal(before, OnDiskBytes());
    }

    [Fact]
    public void A_case_insensitive_serialiser_replaces_the_value_behind_the_files_own_spelling()
    {
        Given("""
            {
              "version": 1,
              "general": { "startminimised": true, "label": "kept" }
            }
            """);

        var store = Create();
        var general = store.Section<GeneralSection>("general");

        Assert.True(general.Read().StartMinimised);
        Assert.Equal("kept", general.Read().Label);
        Assert.True(general.Update(g => g.StartMinimised = false).Saved);

        var after = OnDisk();
        Assert.Contains("\"startminimised\": false", after, StringComparison.Ordinal);
        Assert.DoesNotContain("\"StartMinimised\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_key_the_file_spells_differently_is_refused_rather_than_stamped_beside_it()
    {
        Given("""
            {
              "Version": 1,
              "general": { "Label": "kept" }
            }
            """);
        var before = OnDiskBytes();

        var result = Create().Section<GeneralSection>("general").Update(g => g.Retries = 9);

        Assert.False(result.Saved);
        var conflict = Assert.IsType<SettingsKeyCaseConflictException>(result.Error);
        Assert.Equal("version", conflict.Wanted);
        Assert.Equal("Version", conflict.Found);
        Assert.Equal(before, OnDiskBytes());
    }

    [Fact]
    public void A_move_into_a_section_the_old_document_carries_under_another_case_is_refused()
    {
        var source = Path.Combine(Root, "old.json");
        File.WriteAllText(source, """
            {
              "MQTT": { "Host": "kept" },
              "pollSeconds": 30
            }
            """);

        var result = SettingsMigration.Run(new SettingsMigrationRequest(source, FilePath)
        {
            Moves = [new SettingsSectionMove("mqtt", ["pollSeconds"])],
        });

        Assert.Equal(SettingsMigrationOutcome.RequestRefused, result.Outcome);
        Assert.Contains("differs from the section 'mqtt' only in case", result.Error?.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void A_move_naming_a_key_the_old_document_carries_under_another_case_is_refused()
    {
        var source = Path.Combine(Root, "old.json");
        File.WriteAllText(source, """
            {
              "pollSeconds": 30,
              "graphSpan": "P7D"
            }
            """);

        var result = SettingsMigration.Run(new SettingsMigrationRequest(source, FilePath)
        {
            Moves = [new SettingsSectionMove("general", ["PollSeconds"])],
        });

        Assert.Equal(SettingsMigrationOutcome.RequestRefused, result.Outcome);
        Assert.Contains("differs from it only in case", result.Error?.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void A_move_naming_a_key_the_old_document_does_not_carry_at_all_is_ordinary()
    {
        var source = Path.Combine(Root, "old.json");
        File.WriteAllText(source, """{ "pollSeconds": 30 }""");

        var result = SettingsMigration.Run(new SettingsMigrationRequest(source, FilePath)
        {
            Moves = [new SettingsSectionMove("general", ["pollSeconds", "somethingThisFileNeverHad"])],
        });

        Assert.True(result.Migrated);
        Assert.Equal(["pollSeconds"], result.Carried);
    }

    [Fact]
    public void Two_moves_naming_sections_that_differ_only_in_case_are_refused()
    {
        var source = Path.Combine(Root, "old.json");
        File.WriteAllText(source, """{ "pollSeconds": 30, "graphSpan": "P7D" }""");

        var result = SettingsMigration.Run(new SettingsMigrationRequest(source, FilePath)
        {
            Moves =
            [
                new SettingsSectionMove("general", ["pollSeconds"]),
                new SettingsSectionMove("General", ["graphSpan"]),
            ],
        });

        Assert.Equal(SettingsMigrationOutcome.RequestRefused, result.Outcome);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Two_moves_claiming_one_key_under_two_spellings_are_refused()
    {
        // The document carries neither spelling, so the only guard that can catch this is the one
        // reading the request itself.
        var source = Path.Combine(Root, "old.json");
        File.WriteAllText(source, """{ "somethingElse": 30 }""");

        var result = SettingsMigration.Run(new SettingsMigrationRequest(source, FilePath)
        {
            Moves =
            [
                new SettingsSectionMove("general", ["pollSeconds"]),
                new SettingsSectionMove("window", ["PollSeconds"]),
            ],
        });

        Assert.Equal(SettingsMigrationOutcome.RequestRefused, result.Outcome);
        Assert.False(File.Exists(FilePath));
    }
}
