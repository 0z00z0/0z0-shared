using System.Text;
using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>A file that could not be read may still be intact, so nothing is written over it. The
/// latch is on "has any read ever succeeded", set once and never cleared — a later read that fails,
/// or finds the document broken, must not stop a good configuration being written back.</summary>
public sealed class SectionLatchTests : SectionedTestBase
{
    [Fact]
    public void A_document_held_open_at_construction_is_not_written_over()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");
        var before = OnDiskBytes();

        using var seized = Seize();
        var store = Create();

        Assert.False(store.HasLoaded);

        var result = store.Section<GeneralSection>("general").Update(g => g.Retries = 1);
        Assert.False(result.Saved);
        Assert.IsType<InvalidOperationException>(result.Error);

        seized.Dispose();
        Assert.Equal(before, OnDiskBytes());
    }

    [Fact]
    public void No_copy_is_taken_of_a_document_that_could_not_be_read()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");

        using var seized = Seize();
        _ = Create();

        Assert.Empty(QuarantineCopies());
    }

    [Fact]
    public void A_reload_that_succeeds_lifts_the_refusal()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");

        var seized = Seize();
        var store = Create();
        Assert.False(store.HasLoaded);

        seized.Dispose();
        store.Reload();

        Assert.True(store.HasLoaded);
        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 8).Saved);
        Assert.Contains("\"Retries\": 8", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_document_counts_as_a_read_because_there_is_nothing_to_lose()
    {
        var store = Create();

        Assert.True(store.HasLoaded);
        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 4).Saved);
        Assert.Contains("\"Retries\": 4", OnDisk(), StringComparison.Ordinal);
        Assert.Contains("\"version\": 1", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_broken_by_hand_is_written_over_once_a_read_has_succeeded()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");

        var store = Create();
        Assert.True(store.HasLoaded);

        File.WriteAllText(FilePath, "{ this is not json at all");

        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 9).Saved);
        Assert.Contains("\"Retries\": 9", OnDisk(), StringComparison.Ordinal);
        Assert.Single(QuarantineCopies());
        Assert.Equal("{ this is not json at all", File.ReadAllText(QuarantineCopies()[0]));
    }

    [Fact]
    public void A_write_is_refused_while_the_document_is_held_open_even_after_a_good_read()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");

        var store = Create();
        Assert.True(store.HasLoaded);

        using var seized = Seize();
        var result = store.Section<GeneralSection>("general").Update(g => g.Retries = 9);

        Assert.False(result.Saved);
        Assert.IsType<IOException>(result.Error);
    }

    [Fact]
    public void A_refused_write_is_announced()
    {
        Given("""{ "version": 1, "general": { "Retries": 7 } }""");

        using var seized = Seize();
        var store = Create();

        SettingsSaveFailedEventArgs? announced = null;
        store.SaveFailed += (_, args) => announced = args;

        store.Section<GeneralSection>("general").Update(g => g.Retries = 1);

        Assert.NotNull(announced);
        Assert.Equal(FilePath, announced.FilePath);
    }

    [Fact]
    public void An_empty_document_counts_as_a_read()
    {
        GivenBytes(Encoding.UTF8.GetBytes("   \r\n  "));

        var store = Create();

        Assert.True(store.HasLoaded);
        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 4).Saved);
        Assert.Empty(QuarantineCopies());
    }
}
