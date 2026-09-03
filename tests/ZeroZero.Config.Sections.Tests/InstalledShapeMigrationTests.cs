using System.Text;
using System.Text.Json;
using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>The migration against a settings file shaped like an installed one.</summary>
/// <remarks>
/// <para>An installed settings file of the application this component is being built for is already
/// section-addressed and version-stamped: a scalar version first, then ten objects, section keys in
/// lower case with underscores, member names in upper camel case, no byte-order mark, no comments and
/// no trailing commas. The shape was measured by that application's own session and reported. Nothing
/// here reads any installed file, and the member names inside the sections are this fixture's own
/// except the three that were reported because they defeat a binder — and the endpoint memory, whose
/// members are those of <c>ZeroZero.Mqtt</c>'s own <c>MqttEndpointMemory</c>, because that type is
/// declared in this repository and the section would otherwise depict it under names it cannot
/// produce. Its <c>Encrypted</c> is deliberately present and true: the type holds it as a nullable
/// so that absent means "not recorded" rather than "plain".</para>
/// <para>So the flat-to-sectioned move is not what a current installation needs. What the migration
/// does for an already-sectioned document is carry every key across into a new file and stamp the
/// version this build asks for — the one thing the store deliberately refuses to do — while leaving
/// the old file exactly where it was. These tests prove that, and prove the member spellings that
/// defeat a binder survive it unchanged.</para>
/// </remarks>
public sealed class InstalledShapeMigrationTests : SectionedTestBase
{
    private static readonly string[] Sections =
    [
        "general", "graph", "smart_charge", "network", "keep_awake",
        "lid_close", "notifications", "mqtt", "diagnostics", "window",
    ];

    private static readonly JsonReaderOptions Strict = new() { CommentHandling = JsonCommentHandling.Disallow };

    private string SourcePath => Path.Combine(Root, "installed-settings.json");

    public InstalledShapeMigrationTests() =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "installed-settings.json"), SourcePath);

    [Fact]
    public void The_fixture_is_the_shape_that_was_measured()
    {
        var content = File.ReadAllBytes(SourcePath);

        Assert.NotEqual(Encoding.UTF8.GetPreamble(), content[..3]);
        Assert.Empty(CommentsIn(content));
        Assert.True(Parses(content, Strict));

        var root = JsonObjectSpans.TryReadDocument(content)!;
        Assert.Equal(["version", .. Sections], root.Members.Select(static member => member.Name));
    }

    [Fact]
    public void Every_section_arrives_in_the_files_own_order_and_the_version_is_the_requested_one()
    {
        var result = Run(version: 2);

        Assert.True(result.Migrated);
        Assert.Equal(Sections, result.Carried);

        var root = JsonObjectSpans.TryReadDocument(OnDiskBytes())!;
        Assert.Equal(["version", .. Sections], root.Members.Select(static member => member.Name));
        Assert.Equal(2, new SectionedSettingsFile(InstalledOptions(2)).DocumentVersion);
    }

    [Fact]
    public void Every_section_arrives_with_the_bytes_the_old_file_held()
    {
        var source = File.ReadAllBytes(SourcePath);
        Assert.True(Run().Migrated);

        var target = OnDiskBytes();
        var from = JsonObjectSpans.TryReadDocument(source)!;
        var to = JsonObjectSpans.TryReadDocument(target)!;

        foreach (var member in from.Members.Where(static member => member.Name != "version"))
        {
            var landed = to.Find(member.Name)!.Value;
            Assert.Equal(
                Compact(source, member.ValueStart, member.ValueEnd),
                Compact(target, landed.ValueStart, landed.ValueEnd));
        }
    }

    [Fact]
    public void The_member_spellings_that_defeat_a_binder_are_not_corrected()
    {
        Assert.True(Run().Migrated);

        var target = OnDisk();
        Assert.Contains("\"GraphLineColouring\"", target, StringComparison.Ordinal);
        Assert.DoesNotContain("\"GraphLineColoring\"", target, StringComparison.Ordinal);
        Assert.Contains("\"LidDelaySavedAcAction\"", target, StringComparison.Ordinal);
        Assert.Contains("\"LidDelaySavedDcAction\"", target, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LidDelaySavedACAction\"", target, StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_file_has_no_byte_order_mark_and_no_comment_and_a_strict_reader_opens_it()
    {
        var result = Run();

        Assert.True(result.Migrated);
        Assert.Empty(result.CommentsNotCarried);
        Assert.Empty(result.CommentsInsideValues);

        var target = OnDiskBytes();
        Assert.NotEqual(Encoding.UTF8.GetPreamble(), target[..3]);
        Assert.Empty(CommentsIn(target));
        Assert.True(Parses(target, Strict));
    }

    [Fact]
    public void The_old_file_is_byte_for_byte_what_it_was()
    {
        var before = File.ReadAllBytes(SourcePath);
        var stamp = File.GetLastWriteTimeUtc(SourcePath);

        Assert.True(Run().Migrated);

        Assert.Equal(before, File.ReadAllBytes(SourcePath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(SourcePath));
    }

    [Fact]
    public void A_type_spelling_its_members_the_way_the_file_does_reads_them_case_sensitively()
    {
        Assert.True(Run().Migrated);

        var store = new SectionedSettingsFile(InstalledOptions() with { Serialiser = CaseSensitive() });

        var graph = store.Section<InstalledGraphSection>("graph").Read();
        Assert.Equal("ByState", graph.GraphLineColouring);

        var lid = store.Section<InstalledLidCloseSection>("lid_close").Read();
        Assert.Equal("Sleep", lid.LidDelaySavedAcAction);
        Assert.Equal("Hibernate", lid.LidDelaySavedDcAction);
    }

    [Fact]
    public void A_type_spelling_a_member_with_the_initialism_reads_nothing_and_its_write_is_refused()
    {
        Assert.True(Run().Migrated);
        var before = OnDiskBytes();

        var store = new SectionedSettingsFile(InstalledOptions() with { Serialiser = CaseSensitive() });
        var lid = store.Section<InitialismLidCloseSection>("lid_close");

        // Silently empty on the read: this is the failure the refusal on the write makes loud.
        Assert.Equal(string.Empty, lid.Read().LidDelaySavedACAction);

        var result = lid.Update(l => l.LidDelaySeconds = 60);

        Assert.False(result.Saved);
        var conflict = Assert.IsType<SettingsKeyCaseConflictException>(result.Error);
        Assert.Equal("LidDelaySavedACAction", conflict.Wanted);
        Assert.Equal("LidDelaySavedAcAction", conflict.Found);
        Assert.Equal(before, OnDiskBytes());
    }

    [Fact]
    public void A_type_spelling_a_member_as_a_different_word_is_not_a_case_clash_and_is_not_caught()
    {
        Assert.True(Run().Migrated);

        var store = new SectionedSettingsFile(InstalledOptions() with { Serialiser = CaseSensitive() });
        var graph = store.Section<AmericanGraphSection>("graph");

        Assert.Equal(string.Empty, graph.Read().GraphLineColoring);
        Assert.True(graph.Update(g => g.GraphLineColoring = "ByState").Saved);

        // The limit, stated rather than hidden: colour and color are two words, not two cases, so
        // nothing can tell that one was meant to be the other. Both are in the file now, and the
        // application's own reader takes whichever its type declares.
        var after = OnDisk();
        Assert.Contains("\"GraphLineColouring\": \"ByState\"", after, StringComparison.Ordinal);
        Assert.Contains("\"GraphLineColoring\": \"ByState\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_one_section_of_the_new_file_leaves_the_other_nine_byte_for_byte()
    {
        Assert.True(Run().Migrated);
        var before = OnDiskBytes();

        var store = new SectionedSettingsFile(InstalledOptions());
        Assert.True(store.Section<WindowSection>("window").Update(w => w.Width = 1000).Saved);

        var after = OnDiskBytes();
        var from = JsonObjectSpans.TryReadDocument(before)!;
        var to = JsonObjectSpans.TryReadDocument(after)!;

        foreach (var name in Sections.Where(static name => name != "window"))
        {
            var was = from.Find(name)!.Value;
            var now = to.Find(name)!.Value;
            Assert.Equal(
                JsonObjectSpans.Text(before, was.ValueStart, was.ValueEnd),
                JsonObjectSpans.Text(after, now.ValueStart, now.ValueEnd));
        }

        Assert.Contains("\"Width\": 1000", OnDisk(), StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CaseSensitive()
    {
        var options = SettingsFileOptions.CreateSerialiser();
        options.PropertyNameCaseInsensitive = false;
        return options;
    }

    private SectionedSettingsOptions InstalledOptions(int version = 1) =>
        new(Root, FileName) { Version = version, SectionOrder = Sections };

    private SettingsMigrationResult Run(int version = 1) =>
        SettingsMigration.Run(new SettingsMigrationRequest(SourcePath, FilePath) { Version = version });

    private static List<string> CommentsIn(byte[] content)
    {
        var options = new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow, AllowTrailingCommas = true };
        var reader = new Utf8JsonReader(content, options);
        var found = new List<string>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.Comment) found.Add(reader.GetComment());
        }

        return found;
    }

    private static bool Parses(byte[] content, JsonReaderOptions options)
    {
        try
        {
            var reader = new Utf8JsonReader(content, options);
            while (reader.Read())
            {
                // Reading to the end is the check: the reader throws on what it will not accept.
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Compact(byte[] content, int start, int end)
    {
        var text = new StringBuilder();
        var reader = new Utf8JsonReader(content.AsSpan(start..end), JsonObjectSpans.ReaderOptions);
        while (reader.Read()) text.Append(reader.TokenType).Append(Encoding.UTF8.GetString(reader.ValueSpan)).Append('|');
        return text.ToString();
    }
}
