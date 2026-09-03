using System.Text;
using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>The route from an old settings file to a new one: the new file is written, the old one
/// is not touched at all, and the new one is read back and checked before success is reported.</summary>
public sealed class MigrationTests : SectionedTestBase
{
    private string SourcePath => Path.Combine(Root, "old-settings.json");

    [Fact]
    public void The_old_file_is_not_touched_by_a_migration_that_succeeds()
    {
        var before = GivenSource("""
            {
              "startMinimised": true,
              "graphSpan": "P7D"
            }
            """);
        var stamp = File.GetLastWriteTimeUtc(SourcePath);

        var result = SettingsMigration.Run(Request(new SettingsSectionMove("general", ["startMinimised"])));

        Assert.True(result.Migrated);
        Assert.True(File.Exists(SourcePath));
        Assert.Equal(before, File.ReadAllBytes(SourcePath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(SourcePath));
    }

    [Fact]
    public void A_mapped_key_lands_in_its_section_and_an_unmapped_one_stays_at_the_top()
    {
        GivenSource("""
            {
              "startMinimised": true,
              "somethingNobodyMaps": { "Deep": [1, 2] }
            }
            """);

        var result = SettingsMigration.Run(Request(new SettingsSectionMove("general", ["startMinimised"])));

        Assert.True(result.Migrated);
        Assert.Equal(["startMinimised", "somethingNobodyMaps"], result.Carried);

        var target = OnDisk();
        Assert.Contains("\"general\": {", target, StringComparison.Ordinal);
        Assert.Contains("\"startMinimised\": true", target, StringComparison.Ordinal);
        Assert.Contains("\"somethingNobodyMaps\"", target, StringComparison.Ordinal);
        Assert.Contains("\"Deep\": [1, 2]", target, StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_document_declares_the_requested_version_first()
    {
        GivenSource("""{ "startMinimised": true }""");

        SettingsMigration.Run(Request(new SettingsSectionMove("general", ["startMinimised"])) with { Version = 3 });

        var store = new SectionedSettingsFile(Options());
        Assert.Equal(3, store.DocumentVersion);
        Assert.Equal("version", store.Keys[0]);
    }

    [Fact]
    public void An_existing_new_document_is_left_alone()
    {
        GivenSource("""{ "startMinimised": true }""");
        Given("""{ "version": 1, "general": { "StartMinimised": false } }""");
        var before = OnDiskBytes();

        var result = SettingsMigration.Run(Request());

        Assert.Equal(SettingsMigrationOutcome.TargetAlreadyExists, result.Outcome);
        Assert.Equal(before, OnDiskBytes());
    }

    [Fact]
    public void A_missing_old_document_is_reported_and_nothing_is_written()
    {
        var result = SettingsMigration.Run(Request());

        Assert.Equal(SettingsMigrationOutcome.SourceMissing, result.Outcome);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void An_old_document_that_cannot_be_read_is_reported_and_nothing_is_written()
    {
        GivenSource("""{ "startMinimised": true }""");

        using var seized = new FileStream(SourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = SettingsMigration.Run(Request());

        Assert.Equal(SettingsMigrationOutcome.SourceUnreadable, result.Outcome);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void An_old_document_that_is_not_json_is_reported_and_nothing_is_written()
    {
        GivenSource("this was never a settings file");

        var result = SettingsMigration.Run(Request());

        Assert.Equal(SettingsMigrationOutcome.SourceNotADocument, result.Outcome);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void The_same_path_twice_is_refused()
    {
        GivenSource("""{ "startMinimised": true }""");

        var result = SettingsMigration.Run(new SettingsMigrationRequest(SourcePath, SourcePath));

        Assert.Equal(SettingsMigrationOutcome.RequestRefused, result.Outcome);
        Assert.IsType<ArgumentException>(result.Error);
    }

    [Fact]
    public void One_key_claimed_by_two_sections_is_refused()
    {
        GivenSource("""{ "startMinimised": true }""");

        var result = SettingsMigration.Run(Request(
            new SettingsSectionMove("general", ["startMinimised"]),
            new SettingsSectionMove("window", ["startMinimised"])));

        Assert.Equal(SettingsMigrationOutcome.RequestRefused, result.Outcome);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void A_section_whose_name_the_old_document_already_uses_is_refused()
    {
        GivenSource("""{ "general": { "Retries": 3 }, "startMinimised": true }""");

        var result = SettingsMigration.Run(Request(new SettingsSectionMove("general", ["startMinimised"])));

        Assert.Equal(SettingsMigrationOutcome.RequestRefused, result.Outcome);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void A_document_that_is_already_sectioned_is_carried_across_with_no_moves()
    {
        GivenSource("""
            {
              "version": 1,
              "general": { "Retries": 7 },
              "graph": { "Span": "P7D" }
            }
            """);

        var result = SettingsMigration.Run(Request() with { Version = 2 });

        Assert.True(result.Migrated);
        Assert.Equal(["general", "graph"], result.Carried);

        var store = new SectionedSettingsFile(Options(version: 2));
        Assert.Equal(2, store.DocumentVersion);
        Assert.Equal(7, store.Section<GeneralSection>("general").Read().Retries);
        Assert.Equal("P7D", store.Section<GraphSection>("graph").Read().Span);
    }

    [Fact]
    public void A_comment_travels_with_the_key_it_sits_above()
    {
        GivenSource("""
            {
              // raised after the December outage
              "pollSeconds": 45,
              "graphSpan": "P7D"
            }
            """);

        var result = SettingsMigration.Run(Request(new SettingsSectionMove("general", ["pollSeconds"])));

        Assert.True(result.Migrated);
        Assert.Equal(1, result.Comments);

        var target = OnDisk();
        var comment = target.IndexOf("// raised after the December outage", StringComparison.Ordinal);
        var key = target.IndexOf("\"pollSeconds\"", StringComparison.Ordinal);
        Assert.True(comment >= 0 && comment < key);
    }

    [Fact]
    public void A_comment_inside_a_value_travels_inside_that_value()
    {
        GivenSource("""
            {
              "retired": {
                /* nobody remembers what this did */
                "Mode": "Scorching"
              }
            }
            """);

        var result = SettingsMigration.Run(Request());

        Assert.True(result.Migrated);
        Assert.Contains("/* nobody remembers what this did */", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_document_that_cannot_be_written_is_reported_and_the_old_one_stands()
    {
        var before = GivenSource("""{ "startMinimised": true }""");

        // A directory where the file should go is a write the file system will not do.
        Directory.CreateDirectory(FilePath);

        var result = SettingsMigration.Run(Request());

        Assert.Equal(SettingsMigrationOutcome.WriteFailed, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.Equal(before, File.ReadAllBytes(SourcePath));
    }

    [Fact]
    public void The_byte_order_mark_and_line_ending_of_the_old_document_are_the_new_ones()
    {
        File.WriteAllBytes(
            SourcePath,
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("{\n    \"startMinimised\": true\n}")]);

        Assert.True(SettingsMigration.Run(Request(new SettingsSectionMove("general", ["startMinimised"]))).Migrated);

        var target = OnDiskBytes();
        Assert.Equal(Encoding.UTF8.GetPreamble(), target[..3]);

        var text = Encoding.UTF8.GetString(target);
        Assert.DoesNotContain("\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\n    \"general\": {\n        \"startMinimised\": true", text, StringComparison.Ordinal);
    }

    private SettingsMigrationRequest Request(params SettingsSectionMove[] moves) =>
        new(SourcePath, FilePath) { Moves = moves };

    private byte[] GivenSource(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(SourcePath, bytes);
        return bytes;
    }
}
