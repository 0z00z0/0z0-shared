using System.Text;
using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>The point of the whole design: writing one section changes that section and nothing
/// else. Each test writes a real document, saves one section, and asserts on the bytes.</summary>
public sealed class SectionPreservationTests : SectionedTestBase
{
    private const string Document = """
        {
          "version": 1,
          "general": {
            "StartMinimised": true,
            "Label": "desk",
            "Retries": 3,
            "Mode": "Warm",
            "Groups": {}
          },
          "graph": {
            "Span": "P7D",
            "Points": 48
          },
          "from_a_build_that_no_longer_exists": {
            "Threshold": 0.75,
            "Never": [1, 2, 3]
          }
        }
        """;

    [Fact]
    public void A_sibling_section_is_untouched_byte_for_byte()
    {
        Given(Document);
        var before = OnDisk();

        var store = Create();
        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 9).Saved);

        var after = OnDisk();
        Assert.Equal(Slice(before, "\"graph\""), Slice(after, "\"graph\""));
        Assert.Equal(
            Slice(before, "\"from_a_build_that_no_longer_exists\""),
            Slice(after, "\"from_a_build_that_no_longer_exists\""));
    }

    [Fact]
    public void A_section_this_build_has_no_type_for_survives_a_save()
    {
        Given(Document);

        var store = Create();
        store.Section<GeneralSection>("general").Update(g => g.Label = "cabin");

        Assert.Contains("\"from_a_build_that_no_longer_exists\"", OnDisk(), StringComparison.Ordinal);
        Assert.Contains("\"Threshold\": 0.75", OnDisk(), StringComparison.Ordinal);
        Assert.Contains("\"Never\": [1, 2, 3]", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_changed_member_moves()
    {
        Given(Document);
        var before = OnDisk();

        var store = Create();
        store.Section<GeneralSection>("general").Update(g => g.Retries = 9);

        var after = OnDisk();
        Assert.Equal(before.Replace("\"Retries\": 3", "\"Retries\": 9", StringComparison.Ordinal), after);
    }

    [Fact]
    public void Key_order_is_the_files_own_and_no_type_declares_it()
    {
        Given("""
            {
              "graph": { "Span": "P7D", "Points": 48 },
              "version": 1,
              "general": { "Retries": 3 }
            }
            """);

        var store = Create();
        store.Section<GeneralSection>("general").Update(g => g.Retries = 4);

        Assert.Equal(["graph", "version", "general"], store.Keys);
        Assert.True(
            OnDisk().IndexOf("\"graph\"", StringComparison.Ordinal) <
            OnDisk().IndexOf("\"version\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hand_written_comment_survives_a_save()
    {
        Given("""
            {
              // the cabin machine wants this off
              "version": 1,
              "general": {
                /* raised after the December outage */
                "Retries": 7
              }
            }
            """);

        var store = Create();
        store.Section<GeneralSection>("general").Update(g => g.Retries = 8);

        var after = OnDisk();
        Assert.Contains("// the cabin machine wants this off", after, StringComparison.Ordinal);
        Assert.Contains("/* raised after the December outage */", after, StringComparison.Ordinal);
        Assert.Contains("\"Retries\": 8", after, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_member_inside_the_owned_section_survives()
    {
        Given("""
            {
              "version": 1,
              "general": {
                "Retries": 3,
                "SomethingOnlyTheOtherBuildKnows": "keep me"
              }
            }
            """);

        var store = Create();
        store.Section<GeneralSection>("general").Update(g => g.Retries = 5);

        Assert.Contains("\"SomethingOnlyTheOtherBuildKnows\": \"keep me\"", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_trailing_comma_survives_a_save()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 3 },
            }
            """);

        var store = Create();
        store.Section<GeneralSection>("general").Update(g => g.Retries = 6);

        Assert.Contains("},\r\n}", OnDisk().ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_order_mark_survives_a_save()
    {
        GivenBytes([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("{\n  \"general\": { \"Retries\": 3 }\n}")]);

        var store = Create();
        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 4).Saved);

        var after = OnDiskBytes();
        Assert.Equal(Encoding.UTF8.GetPreamble(), after[..3]);
        Assert.Contains("\"Retries\": 4", Encoding.UTF8.GetString(after), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void The_files_own_line_ending_is_the_one_added_content_uses(string newLine)
    {
        Given(string.Join(newLine, "{", "    \"version\": 1,", "    \"general\": {", "        \"Retries\": 3", "    }", "}"));

        var store = Create();
        store.Section<GraphSection>("graph").Update(g => g.Points = 12);

        var after = OnDisk();
        var lineFeeds = after.Count(c => c == '\n');
        var carriageReturns = after.Count(c => c == '\r');

        Assert.True(lineFeeds > 0);
        Assert.Equal(newLine == "\r\n" ? lineFeeds : 0, carriageReturns);
        Assert.Contains("\"graph\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public void The_files_own_indent_is_the_one_added_content_uses()
    {
        Given(string.Join("\n", "{", "    \"version\": 1,", "    \"general\": {", "        \"Retries\": 3", "    }", "}"));

        var store = Create();
        store.Section<GraphSection>("graph").Update(g => g.Points = 12);

        Assert.Contains("\n    \"graph\": {\n        \"Span\"", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_the_document_lacks_lands_in_its_declared_slot()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 3 },
              "window": { "Width": 640, "Height": 480 }
            }
            """);

        var store = Create();
        store.Section<GraphSection>("graph").Update(g => g.Points = 12);

        Assert.Equal(["version", "general", "graph", "window"], store.Keys);
    }

    [Fact]
    public void A_section_the_order_does_not_name_is_appended_last()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 3 }
            }
            """);

        var store = Create();
        store.Section<WindowSection>("not_in_the_order").Update(w => w.Width = 1024);

        Assert.Equal(["version", "general", "not_in_the_order"], store.Keys);
    }

    [Fact]
    public void A_member_the_file_lacks_is_appended_rather_than_the_section_rewritten()
    {
        Given("""
            {
              "version": 1,
              "general": {
                "Retries": 3
              }
            }
            """);

        var store = Create();
        store.Section<GeneralSection>("general").Write(new GeneralSection { Retries = 3, Label = "added" });

        var after = OnDisk();
        Assert.Contains("\"Retries\": 3", after, StringComparison.Ordinal);
        Assert.Contains("\"Label\": \"added\"", after, StringComparison.Ordinal);
        Assert.True(
            after.IndexOf("\"Retries\"", StringComparison.Ordinal) < after.IndexOf("\"Label\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_section_a_hand_edit_left_twice_reads_and_writes_the_last_of_them()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 3 },
              "general": { "Retries": 11 }
            }
            """);

        var store = Create();
        var general = store.Section<CounterSection>("general");
        Assert.Equal(11, general.Read().Retries);
        Assert.True(general.Update(g => g.Retries = 12).Saved);

        var after = OnDisk();
        Assert.Contains("\"general\": { \"Retries\": 3 }", after, StringComparison.Ordinal);
        Assert.Contains("\"general\": { \"Retries\": 12 }", after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_a_hand_edit_left_twice_is_written_at_the_last_of_them()
    {
        Given("""{ "version": 1, "general": { "Retries": 3, "Retries": 11 } }""");

        var store = Create();
        var general = store.Section<CounterSection>("general");
        Assert.Equal(11, general.Read().Retries);
        Assert.True(general.Update(g => g.Retries = 12).Saved);

        Assert.Contains("\"Retries\": 3, \"Retries\": 12", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_save_that_changes_nothing_does_not_write_at_all()
    {
        Given("""{ "version": 1, "general": { "Retries": 3 } }""");
        var store = Create();
        var general = store.Section<CounterSection>("general");

        // A read-only file can be read but not replaced, so a write that happens at all is refused:
        // this is how "nothing changed, so nothing was written" is observed from outside.
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        try
        {
            Assert.True(general.Update(g => g.Retries = 3).Saved);
            Assert.False(general.Update(g => g.Retries = 4).Saved);
        }
        finally
        {
            File.SetAttributes(FilePath, FileAttributes.Normal);
        }
    }


    [Fact]
    public void A_save_that_changes_nothing_leaves_the_bytes_alone()
    {
        Given(Document);
        var before = OnDiskBytes();

        var store = Create();
        Assert.True(store.Section<GeneralSection>("general").Update(g => g.Retries = 3).Saved);

        Assert.Equal(before, OnDiskBytes());
    }

    [Fact]
    public void The_version_key_is_stamped_first_when_the_document_has_none()
    {
        Given("""
            {
              "general": { "Retries": 3 }
            }
            """);

        var store = Create();
        store.Section<GeneralSection>("general").Update(g => g.Retries = 4);

        Assert.Equal(["version", "general"], store.Keys);
        Assert.Equal(1, store.DocumentVersion);
    }

    [Fact]
    public void An_existing_lower_version_is_left_where_it_is()
    {
        Given("""
            {
              "version": 1,
              "general": { "Retries": 3 }
            }
            """);

        var store = Create(version: 4);
        store.Section<GeneralSection>("general").Update(g => g.Retries = 4);

        Assert.Equal(1, store.DocumentVersion);
        Assert.Contains("\"version\": 1", OnDisk(), StringComparison.Ordinal);
    }

    // The text from a key to the end of the line it sits on, which is enough to compare one section
    // of a small document against another rendering of it.
    private static string Slice(string document, string key)
    {
        var at = document.IndexOf(key, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{key} is not in the document.");

        var depth = 0;
        for (var i = at; i < document.Length; i++)
        {
            if (document[i] == '{') depth++;
            else if (document[i] == '}' && --depth == 0) return document[at..(i + 1)];
        }

        return document[at..];
    }
}
