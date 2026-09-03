using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>The version key's asymmetric handling, and what a reload announces.</summary>
public sealed class SectionVersionAndReloadTests : SectionedTestBase
{
    [Fact]
    public void A_document_from_a_newer_build_is_neither_read_nor_written()
    {
        Given("""
            {
              "version": 9,
              "general": { "Retries": 7 }
            }
            """);
        var before = OnDiskBytes();

        var store = Create(version: 1);

        Assert.True(store.IsFromNewerVersion);
        Assert.Equal(3, store.Section<GeneralSection>("general").Read().Retries);

        var result = store.Section<GeneralSection>("general").Update(g => g.Retries = 1);
        Assert.False(result.Saved);
        Assert.Equal(before, OnDiskBytes());
    }

    [Fact]
    public void A_document_from_an_older_build_is_read_as_it_stands()
    {
        Given("""
            {
              "general": { "Retries": 7 }
            }
            """);

        var store = Create(version: 3);

        Assert.False(store.IsFromNewerVersion);
        Assert.Null(store.DocumentVersion);
        Assert.Equal(7, store.Section<GeneralSection>("general").Read().Retries);
    }

    [Fact]
    public void A_document_that_becomes_newer_between_the_read_and_the_write_is_not_written_over()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");

        var store = Create(version: 1);
        Assert.False(store.IsFromNewerVersion);

        File.WriteAllText(FilePath, """{ "version": 9, "general": { "Retries": 7 }, "future": { "New": true } }""");
        var before = OnDiskBytes();

        var result = store.Section<GeneralSection>("general").Update(g => g.Retries = 1);

        Assert.False(result.Saved);
        Assert.True(store.IsFromNewerVersion);
        Assert.Equal(before, OnDiskBytes());
    }

    [Fact]
    public void A_reload_announces_only_the_sections_whose_bytes_moved()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 7 },
              "graph": { "Span": "P7D" }
            }
            """);

        var store = Create();
        var general = store.Section<GeneralSection>("general");
        var graph = store.Section<GraphSection>("graph");

        var generalChanged = 0;
        var graphChanged = 0;
        general.Changed += (_, _) => generalChanged++;
        graph.Changed += (_, _) => graphChanged++;

        File.WriteAllText(FilePath, """
            {
              "version": 1,
              "general": { "Retries": 11 },
              "graph": { "Span": "P7D" }
            }
            """);

        Assert.True(store.Reload());
        Assert.Equal(1, generalChanged);
        Assert.Equal(0, graphChanged);
        Assert.Equal(11, general.Read().Retries);
    }

    [Fact]
    public void A_reload_of_an_unchanged_document_announces_nothing()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");

        var store = Create();
        var general = store.Section<GeneralSection>("general");

        var changed = 0;
        general.Changed += (_, _) => changed++;

        Assert.False(store.Reload());
        Assert.Equal(0, changed);
    }

    [Fact]
    public void A_reload_that_cannot_read_the_document_leaves_the_held_state_standing()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");

        var store = Create();
        using var seized = Seize();

        Assert.False(store.Reload());
        Assert.Equal(7, store.Section<GeneralSection>("general").Read().Retries);
    }

    [Fact]
    public void A_write_announces_its_own_section()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 }, "graph": { "Span": "P7D" } }""");

        var store = Create();
        var general = store.Section<GeneralSection>("general");
        var graph = store.Section<GraphSection>("graph");

        var generalChanged = 0;
        var graphChanged = 0;
        general.Changed += (_, _) => generalChanged++;
        graph.Changed += (_, _) => graphChanged++;

        Assert.True(general.Update(g => g.Retries = 8).Saved);

        Assert.Equal(1, generalChanged);
        Assert.Equal(0, graphChanged);
    }

    [Fact]
    public void A_write_builds_on_an_edit_made_out_of_band_rather_than_on_what_memory_holds()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 7 }
            }
            """);

        var store = Create();
        var general = store.Section<GeneralSection>("general");
        Assert.Equal(7, general.Read().Retries);

        File.WriteAllText(FilePath, """
            {
              "version": 1,
              "general": { "Retries": 7, "Label": "edited by hand" },
              "added_by_hand": { "Keep": true }
            }
            """);

        Assert.True(general.Update(g => g.Retries = 12).Saved);

        var after = OnDisk();
        Assert.Contains("\"Label\": \"edited by hand\"", after, StringComparison.Ordinal);
        Assert.Contains("\"added_by_hand\"", after, StringComparison.Ordinal);
        Assert.Contains("\"Retries\": 12", after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_notification_context_carries_the_announcement()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");

        var context = new RecordingContext();
        var store = Create(notificationContext: context);
        var general = store.Section<GeneralSection>("general");

        var announced = false;
        general.Changed += (_, _) => announced = true;

        Assert.True(general.Update(g => g.Retries = 8).Saved);

        Assert.Equal(1, context.Posted);
        Assert.True(announced);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        internal int Posted { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Posted++;
            callback(state);
        }
    }
}
