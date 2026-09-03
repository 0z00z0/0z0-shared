using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>One value this build cannot read costs its own section and nothing else. The document
/// is never bound to a type, so a sibling holding a value no type in this build could accept is
/// walked past as structure and its bytes are carried through.</summary>
public sealed class SectionIsolationTests : SectionedTestBase
{
    [Fact]
    public void An_enum_member_this_build_does_not_know_costs_only_the_section_holding_it()
    {
        Given("""
            {
              "version": 1,
              "general": { "Mode": "Scorching", "Retries": 7 },
              "graph": { "Span": "P30D", "Points": 90 }
            }
            """);

        var store = Create();

        Assert.Equal("P30D", store.Section<GraphSection>("graph").Read().Span);
        Assert.Equal(90, store.Section<GraphSection>("graph").Read().Points);
        Assert.True(store.Section<GeneralSection>("general").IsUnreadable);
        Assert.Equal(3, store.Section<GeneralSection>("general").Read().Retries);
    }

    [Fact]
    public void A_value_of_the_wrong_kind_in_a_sibling_costs_the_reader_nothing()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 7 },
              "graph": { "Points": "not a number at all" }
            }
            """);

        var store = Create();

        Assert.Equal(7, store.Section<GeneralSection>("general").Read().Retries);
        Assert.True(store.Section<GraphSection>("graph").IsUnreadable);
    }

    [Fact]
    public void A_sibling_this_build_cannot_read_survives_a_save_of_another_section()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 7 },
              "graph": { "Points": "not a number at all" }
            }
            """);

        var store = Create();
        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 8).Saved);

        Assert.Contains("\"Points\": \"not a number at all\"", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_the_unreadable_section_repairs_that_section_and_leaves_the_rest()
    {
        Given("""
            {
              "version": 1,
              "general": { "Mode": "Scorching", "Retries": 7 },
              "graph": { "Span": "P30D", "Points": 90 }
            }
            """);

        var store = Create();
        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 8).Saved);

        var after = OnDisk();
        Assert.Contains("\"Mode\": \"Automatic\"", after, StringComparison.Ordinal);
        Assert.Contains("\"Retries\": 8", after, StringComparison.Ordinal);
        Assert.Contains("\"Span\": \"P30D\"", after, StringComparison.Ordinal);
        Assert.Equal(8, store.Section<GeneralSection>("general").Read().Retries);
    }

    [Fact]
    public void The_document_is_copied_aside_before_a_section_it_cannot_read_is_repaired()
    {
        Given("""
            {
              "version": 1,
              "general": { "Mode": "Scorching" }
            }
            """);

        var store = Create();
        _ = store.Section<GeneralSection>("general");

        var copies = QuarantineCopies();
        Assert.Single(copies);
        Assert.Contains("\"Mode\": \"Scorching\"", File.ReadAllText(copies[0]), StringComparison.Ordinal);
        Assert.Equal(copies[0], store.LastQuarantinePath);
    }

    [Fact]
    public void A_section_the_document_lacks_reads_as_defaults_and_is_not_a_failure()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 7 }
            }
            """);

        var store = Create();
        var graph = store.Section<GraphSection>("graph");

        Assert.False(graph.IsPresent);
        Assert.False(graph.IsUnreadable);
        Assert.Equal("P1D", graph.Read().Span);
        Assert.Empty(QuarantineCopies());
    }

    [Fact]
    public void A_key_that_holds_something_other_than_an_object_is_replaced_and_its_siblings_are_not()
    {
        Given("""
            {
              "version": 1,
              "general": 42,
              "graph": { "Span": "P30D" }
            }
            """);

        var store = Create();
        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 5).Saved);

        var after = OnDisk();
        Assert.Contains("\"Retries\": 5", after, StringComparison.Ordinal);
        Assert.Contains("\"Span\": \"P30D\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_that_is_not_a_json_object_is_refused_at_wire_up()
    {
        Given("{}");

        var store = Create();
        var error = Assert.Throws<ArgumentException>(() => store.Section<NotASection>("nope"));
        Assert.Contains("JSON object", error.Message, StringComparison.Ordinal);
    }
}
