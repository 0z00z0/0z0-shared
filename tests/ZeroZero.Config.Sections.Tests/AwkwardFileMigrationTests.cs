using System.Text;
using System.Text.Json;
using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>The migration proved against a settings file with the awkward properties an installed
/// one has: a byte-order mark, hand-written comments, indentation that changed halfway down, a key
/// left in twice by an edit, a section from a build that no longer exists carrying values no type
/// here could bind, a trailing comma, a number written as <c>0.750</c>, non-ASCII text, and keys in
/// no useful order.</summary>
/// <remarks>The file is a fixture rather than a copy of any installed document — the shape is taken
/// from the settings decision record, and no application's own file is read.</remarks>
public sealed class AwkwardFileMigrationTests : SectionedTestBase
{
    private static readonly SettingsSectionMove[] Moves =
    [
        new("general", ["startMinimised", "pollSeconds", "label", "keepAwakeWhileCharging"]),
        new("graph", ["graphSpan", "thresholdWarn"]),
        new("window", ["windowWidth", "windowHeight"]),
        new("notifications", ["notifyOn"]),
    ];

    private string SourcePath => Path.Combine(Root, "awkward-settings.json");

    public AwkwardFileMigrationTests() =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "awkward-settings.json"), SourcePath);

    [Fact]
    public void Every_top_level_key_of_the_old_file_lands_in_the_new_one()
    {
        var result = Run();

        Assert.Equal(SettingsMigrationOutcome.Migrated, result.Outcome);
        Assert.Equal(
            [
                "startMinimised", "pollSeconds", "graphSpan", "legacyBridgeMode", "thresholdWarn",
                "notifyOn", "label", "windowWidth", "windowHeight", "mqtt", "retiredInBuild14",
                "pollSeconds", "keepAwakeWhileCharging",
            ],
            result.Carried);
    }

    [Fact]
    public void Every_value_arrives_with_the_bytes_the_old_file_held()
    {
        var source = File.ReadAllBytes(SourcePath);
        Assert.True(Run().Migrated);

        var target = File.ReadAllBytes(FilePath);
        var from = JsonObjectSpans.TryReadDocument(source)!;
        var to = JsonObjectSpans.TryReadDocument(target)!;

        var into = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var move in Moves)
        {
            foreach (var key in move.Keys) into[key] = move.Section;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in from.Members)
        {
            var holder = into.TryGetValue(member.Name, out var section)
                ? JsonObjectSpans.TryRead(target, Value(to, section))!
                : to;

            seen.TryGetValue(member.Name, out var index);
            seen[member.Name] = index + 1;

            var landed = holder.All(member.Name);
            Assert.True(landed.Count > index, $"{member.Name} did not arrive.");
            Assert.Equal(
                Compact(source, member.ValueStart, member.ValueEnd),
                Compact(target, landed[index].ValueStart, landed[index].ValueEnd));
        }
    }

    [Fact]
    public void The_key_a_hand_edit_left_twice_arrives_twice_with_both_values()
    {
        Assert.True(Run().Migrated);

        var target = File.ReadAllBytes(FilePath);
        var general = JsonObjectSpans.TryRead(target, Value(JsonObjectSpans.TryReadDocument(target)!, "general"))!;

        var polls = general.All("pollSeconds");
        Assert.Equal(2, polls.Count);
        Assert.Equal("30", JsonObjectSpans.Text(target, polls[0].ValueStart, polls[0].ValueEnd));
        Assert.Equal("45", JsonObjectSpans.Text(target, polls[1].ValueStart, polls[1].ValueEnd));
    }

    [Fact]
    public void Every_comment_arrives()
    {
        var result = Run();
        var target = OnDisk();

        Assert.Equal(4, result.Comments);
        Assert.Contains("// Hand-edited on the workshop machine after the December outage.", target, StringComparison.Ordinal);
        Assert.Contains("// Vinterstua — målepunkt øst, satt opp for hånd.", target, StringComparison.Ordinal);
        Assert.Contains("// Left off deliberately on the workshop machine.", target, StringComparison.Ordinal);
        Assert.Contains("/* The broker moved in March; the old block is kept below in case it comes back. */", target, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comment_written_inside_a_value_travels_inside_that_value()
    {
        Assert.True(Run().Migrated);

        var target = OnDisk();
        var comment = target.IndexOf("/* nothing here has been read since build 14 */", StringComparison.Ordinal);
        var section = target.IndexOf("\"retiredInBuild14\"", StringComparison.Ordinal);
        Assert.True(comment > section);
    }

    [Fact]
    public void The_section_from_a_build_that_no_longer_exists_arrives_intact_at_the_top_level()
    {
        Assert.True(Run().Migrated);

        var target = OnDisk();
        Assert.Contains("\"retiredInBuild14\"", target, StringComparison.Ordinal);
        Assert.Contains("\"Mode\": \"Scorching\"", target, StringComparison.Ordinal);
        Assert.Contains("\"escalate\": [1, 2, { \"after\": \"PT15M\" }]", target, StringComparison.Ordinal);
        Assert.Contains("\"legacyBridgeMode\": \"FailoverThenHold\"", target, StringComparison.Ordinal);
    }

    [Fact]
    public void The_number_and_the_non_ascii_text_are_not_reformatted()
    {
        Assert.True(Run().Migrated);

        var target = OnDisk();
        Assert.Contains("\"thresholdWarn\": 0.750", target, StringComparison.Ordinal);
        Assert.Contains("\"Vinterstua \u2014 m\u00e5lepunkt \u00f8st\"", target, StringComparison.Ordinal);
        Assert.Contains("målepunkt øst, satt opp for hånd", target, StringComparison.Ordinal);
    }

    [Fact]
    public void The_byte_order_mark_and_line_ending_are_carried()
    {
        Assert.True(Run().Migrated);

        var target = File.ReadAllBytes(FilePath);
        Assert.Equal(Encoding.UTF8.GetPreamble(), target[..3]);

        var text = Encoding.UTF8.GetString(target);
        Assert.Equal(text.Split("\r\n").Length - 1, text.Split('\n').Length - 1);
    }

    [Fact]
    public void The_old_file_is_byte_for_byte_what_it_was()
    {
        var before = File.ReadAllBytes(SourcePath);
        var stamp = File.GetLastWriteTimeUtc(SourcePath);

        Assert.True(Run().Migrated);

        Assert.True(File.Exists(SourcePath));
        Assert.Equal(before, File.ReadAllBytes(SourcePath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(SourcePath));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Root).Select(Path.GetFileName),
            name => name!.StartsWith("awkward-settings.json", StringComparison.Ordinal)
                && name != "awkward-settings.json");
    }

    [Fact]
    public void The_store_reads_the_new_file_and_writing_one_section_still_keeps_the_rest()
    {
        Assert.True(Run().Migrated);

        var store = new SectionedSettingsFile(Options());
        Assert.Equal(1, store.DocumentVersion);
        Assert.Equal(45, Poll(store));

        var graph = store.Section<MigratedGraphSection>("graph");
        Assert.Equal("P30D", graph.Read().GraphSpan);
        Assert.Equal(0.75, graph.Read().ThresholdWarn);
        Assert.True(graph.Update(g => g.GraphSpan = "P1D").Saved);

        var after = OnDisk();

        // The file's own spelling is kept: a second key differing only in case would take over the
        // read and leave the first one dead.
        Assert.Contains("\"graphSpan\": \"P1D\"", after, StringComparison.Ordinal);
        Assert.DoesNotContain("\"GraphSpan\"", after, StringComparison.Ordinal);
        // The store owns this section, so writing it normalises the one member it holds a value
        // for; nothing outside the section moves.
        Assert.Contains("\"thresholdWarn\": 0.75", after, StringComparison.Ordinal);
        Assert.Contains("\"retiredInBuild14\"", after, StringComparison.Ordinal);
        Assert.Contains("\"legacyBridgeMode\": \"FailoverThenHold\"", after, StringComparison.Ordinal);
        Assert.Contains("// Hand-edited on the workshop machine after the December outage.", after, StringComparison.Ordinal);
        Assert.Contains("målepunkt øst, satt opp for hånd", after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_file_that_lost_a_key_is_refused_and_removed()
    {
        var source = File.ReadAllBytes(SourcePath);

        // What an incomplete carry looks like on disk: everything but one of the old file's keys.
        File.WriteAllText(FilePath, """
            {
              "version": 1,
              "general": { "startMinimised": true, "pollSeconds": 30, "label": "x", "keepAwakeWhileCharging": false },
              "graph": { "graphSpan": "P30D", "thresholdWarn": 0.750 },
              "window": { "windowWidth": 1280, "windowHeight": 860 },
              "notifications": { "notifyOn": ["BridgeLost", "PowerRestored", "DiskFull"] },
              "legacyBridgeMode": "FailoverThenHold",
              "mqtt": { "Enabled": true }
            }
            """);

        var result = SettingsMigration.ProveTarget(new SettingsMigrationRequest(SourcePath, FilePath) { Moves = Moves }, source);

        Assert.Equal(SettingsMigrationOutcome.NotProven, result.Outcome);
        Assert.Contains("retiredInBuild14", result.Missing);
        Assert.False(File.Exists(FilePath));
        Assert.Equal(source, File.ReadAllBytes(SourcePath));
    }

    [Fact]
    public void A_new_file_that_changed_a_value_is_refused_and_removed()
    {
        var source = File.ReadAllBytes(SourcePath);
        Assert.True(Run().Migrated);

        var damaged = OnDisk().Replace("\"windowWidth\": 1280", "\"windowWidth\": 1281", StringComparison.Ordinal);
        File.WriteAllText(FilePath, damaged);

        var result = SettingsMigration.ProveTarget(new SettingsMigrationRequest(SourcePath, FilePath) { Moves = Moves }, source);

        Assert.Equal(SettingsMigrationOutcome.NotProven, result.Outcome);
        Assert.Contains("windowWidth", result.Missing);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void A_new_file_that_dropped_a_comment_is_refused_and_removed()
    {
        var source = File.ReadAllBytes(SourcePath);
        Assert.True(Run().Migrated);

        var damaged = OnDisk().Replace("// Left off deliberately on the workshop machine.", "", StringComparison.Ordinal);
        File.WriteAllText(FilePath, damaged);

        var result = SettingsMigration.ProveTarget(new SettingsMigrationRequest(SourcePath, FilePath) { Moves = Moves }, source);

        Assert.Equal(SettingsMigrationOutcome.NotProven, result.Outcome);
        Assert.Contains("// Left off deliberately on the workshop machine.", result.Missing);
        Assert.False(File.Exists(FilePath));
    }

    private SettingsMigrationResult Run() =>
        SettingsMigration.Run(new SettingsMigrationRequest(SourcePath, FilePath) { Moves = Moves });

    private static int Poll(SectionedSettingsFile store)
    {
        var target = File.ReadAllBytes(store.FilePath);
        var general = JsonObjectSpans.TryRead(target, Value(JsonObjectSpans.TryReadDocument(target)!, "general"))!;
        var last = general.All("pollSeconds")[^1];
        return int.Parse(JsonObjectSpans.Text(target, last.ValueStart, last.ValueEnd));
    }

    private static Range Value(JsonObjectSpan root, string name)
    {
        var member = root.Find(name)!.Value;
        return member.ValueStart..member.ValueEnd;
    }

    // The same value with the whitespace between its tokens taken out, which is the only thing a
    // carry across is allowed to change.
    private static string Compact(byte[] content, int start, int end)
    {
        var text = new StringBuilder();
        var reader = new Utf8JsonReader(content.AsSpan(start..end), JsonObjectSpans.ReaderOptions);
        while (reader.Read()) text.Append(reader.TokenType).Append(Encoding.UTF8.GetString(reader.ValueSpan)).Append('|');
        return text.ToString();
    }
}
