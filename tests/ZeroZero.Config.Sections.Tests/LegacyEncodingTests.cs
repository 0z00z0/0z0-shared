using System.Text;
using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>A file saved in a legacy code page carries "å" as the lone byte 0xE5, which is not
/// UTF-8. A name holding it must not fail the walk, and its raw bytes are carried through.</summary>
public sealed class LegacyEncodingTests : SectionedTestBase
{
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void A_top_level_key_that_is_not_utf8_is_carried_through_a_write()
    {
        GivenBytes(Encoding.Latin1.GetBytes("""{ "ConfigVersion": 1, "blå": { "On": true }, "general": { "Retries": 7 } }"""));

        var general = Create().Section<GeneralSection>("general");

        Assert.Equal(7, general.Read().Retries);
        Assert.True(general.Update(g => g.Retries = 8).Saved);
        Assert.Contains((byte)0xE5, OnDiskBytes());
        Assert.Contains("\"Retries\": 8", OnDisk(), StringComparison.Ordinal);
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void A_key_inside_a_section_that_is_not_utf8_keeps_its_siblings_through_a_write()
    {
        GivenBytes(Encoding.Latin1.GetBytes("""{ "ConfigVersion": 1, "general": { "Retries": 7, "fårge": 1, "Keep": 42 } }"""));

        var general = Create().Section<GeneralSection>("general");

        Assert.True(general.Update(g => g.Retries = 8).Saved);
        Assert.Contains((byte)0xE5, OnDiskBytes());
        Assert.Contains("\"Keep\": 42", OnDisk(), StringComparison.Ordinal);
        Assert.Contains("\"Retries\": 8", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_that_is_not_utf8_costs_only_its_own_section()
    {
        GivenBytes(Encoding.Latin1.GetBytes("""{ "ConfigVersion": 1, "general": { "Label": "blå" }, "graph": { "Points": 12 } }"""));

        var store = Create();

        Assert.True(store.Section<GeneralSection>("general").IsUnreadable);
        Assert.Equal(12, store.Section<GraphSection>("graph").Read().Points);
    }
}
