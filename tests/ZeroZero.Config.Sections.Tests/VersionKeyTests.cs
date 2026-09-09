using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>The key the store writes for itself, and what a document's own top-level keys do beside
/// it.</summary>
/// <remarks>
/// <para>Every document here is the grouped shape a consuming application writes — three sections,
/// seven values each — because a one-key file cannot show that one refused key costs every section
/// at once.</para>
/// <para>The store's key is <c>ConfigVersion</c>. A document's <c>Version</c> is a different word,
/// not a different case, so the two stand side by side and neither is renamed. That holds because
/// top-level keys are matched letter for letter; the serialiser's case-insensitive matching binds a
/// section's members and reaches no further.</para>
/// </remarks>
public sealed class VersionKeyTests : SectionedTestBase
{
    private const string Body =
        """

          "general": {
            "StartMinimised": true,
            "StartWithWindows": false,
            "Theme": "System",
            "Language": "en-GB",
            "Label": "kept",
            "Retries": 3,
            "Mode": "Warm"
          },
          "graph": {
            "GraphSpan": "P30D",
            "GraphLineColouring": "ByState",
            "ShowGrid": true,
            "PointsPerHour": 4,
            "Points": 24,
            "Span": "P7D",
            "Smoothing": 0.25
          },
          "window": {
            "Width": 1280,
            "Height": 860,
            "Left": 120,
            "Top": 80,
            "Maximised": false,
            "Monitor": "DISPLAY1",
            "Scale": 1.5
          }
        }
        """;

    // The grouped document, optionally carrying one top-level key of its own ahead of the sections.
    private static string Grouped(string? ownKey) =>
        "{" + (ownKey is null ? string.Empty : $"\n  \"{ownKey}\": 3,") + Body;

    private string[] KeysOnDisk() =>
        [.. JsonObjectSpans.TryReadDocument(OnDiskBytes())!.Members.Select(static member => member.Name)];

    [Fact]
    public void The_key_this_build_writes_for_itself_is_ConfigVersion()
    {
        Assert.Equal("ConfigVersion", SettingsDocument.VersionKey);
    }

    [Fact]
    public void A_document_with_no_version_key_is_stamped_with_ConfigVersion_first()
    {
        Given(Grouped(null));

        Assert.True(Create().Section<CounterSection>("general").Update(c => c.Retries = 9).Saved);

        Assert.Equal(["ConfigVersion", "general", "graph", "window"], KeysOnDisk());
        Assert.Equal(1, Create().DocumentVersion);
    }

    [Fact]
    public void A_document_declaring_Version_for_its_own_purposes_reads_every_section()
    {
        Given(Grouped("Version"));

        var store = Create();

        // Not the store's key: a top-level key is matched letter for letter, so the document's own
        // Version says nothing about the document's shape.
        Assert.Null(store.DocumentVersion);
        Assert.False(store.IsFromNewerVersion);

        var general = store.Section<GeneralSection>("general").Read();
        Assert.True(general.StartMinimised);
        Assert.Equal("kept", general.Label);
        Assert.Equal(3, general.Retries);
        Assert.Equal(SampleMode.Warm, general.Mode);

        var graph = store.Section<GraphSection>("graph").Read();
        Assert.Equal("P7D", graph.Span);
        Assert.Equal(24, graph.Points);

        var window = store.Section<WindowSection>("window").Read();
        Assert.Equal(1280, window.Width);
        Assert.Equal(860, window.Height);
    }

    [Fact]
    public void A_document_declaring_Version_for_its_own_purposes_is_written_with_nothing_renamed()
    {
        Given(Grouped("Version"));

        Assert.True(Create().Section<CounterSection>("general").Update(c => c.Retries = 9).Saved);

        Assert.Equal(["ConfigVersion", "Version", "general", "graph", "window"], KeysOnDisk());

        var after = OnDisk();
        Assert.Contains("\"Version\": 3", after, StringComparison.Ordinal);
        Assert.Contains("\"ConfigVersion\": 1", after, StringComparison.Ordinal);
        Assert.Contains("\"Retries\": 9", after, StringComparison.Ordinal);

        // The other twenty values are still the file's own, and so is the section that was written.
        Assert.Contains("\"Smoothing\": 0.25", after, StringComparison.Ordinal);
        Assert.Contains("\"Monitor\": \"DISPLAY1\"", after, StringComparison.Ordinal);
        Assert.Contains("\"Language\": \"en-GB\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public void Exactly_one_version_key_is_stamped_however_many_writes_land()
    {
        Given(Grouped("Version"));

        var store = Create();
        var counter = store.Section<CounterSection>("general");
        Assert.True(counter.Update(c => c.Retries = 9).Saved);
        Assert.True(counter.Update(c => c.Retries = 11).Saved);
        Assert.True(store.Section<WindowSection>("window").Update(w => w.Width = 1000).Saved);

        var keys = KeysOnDisk();
        Assert.Single(keys, static key => string.Equals(key, "ConfigVersion", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("version", keys, StringComparer.Ordinal);
        Assert.Equal(["ConfigVersion", "Version", "general", "graph", "window"], keys);
    }

    [Fact]
    public void A_document_already_carrying_ConfigVersion_gains_no_second_one()
    {
        Given(Grouped("ConfigVersion"));

        var store = Create(version: 3);
        Assert.Equal(3, store.DocumentVersion);

        Assert.True(store.Section<CounterSection>("general").Update(c => c.Retries = 9).Saved);

        Assert.Equal(["ConfigVersion", "general", "graph", "window"], KeysOnDisk());
        Assert.Equal(3, Create(version: 3).DocumentVersion);
    }

    [Fact]
    public void A_key_differing_from_ConfigVersion_only_in_case_refuses_every_write_and_the_refusal_is_returned()
    {
        Given(Grouped("Configversion"));
        var before = OnDiskBytes();

        var store = Create();

        // Everything reads, which is what makes the refusal below the only sign anything is wrong.
        Assert.Equal("kept", store.Section<GeneralSection>("general").Read().Label);
        Assert.Equal(1280, store.Section<WindowSection>("window").Read().Width);

        var counter = store.Section<CounterSection>("general");

        // Returned, not raised: a caller that reads neither the result nor the event sees a write
        // that appears to have happened.
        var first = counter.Update(c => c.Retries = 9);
        Assert.False(first.Saved);
        var conflict = Assert.IsType<SettingsKeyCaseConflictException>(first.Error);
        Assert.Equal("ConfigVersion", conflict.Wanted);
        Assert.Equal("Configversion", conflict.Found);

        // And it stands: the second write is refused the same way, so nothing ever reaches the file.
        var second = counter.Update(c => c.Retries = 11);
        Assert.False(second.Saved);
        Assert.IsType<SettingsKeyCaseConflictException>(second.Error);

        Assert.Equal(before, OnDiskBytes());
    }
}
