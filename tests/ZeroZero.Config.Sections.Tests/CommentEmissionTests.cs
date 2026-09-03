using System.Text.Json;
using Xunit;

namespace ZeroZero.Config.Sections.Tests;

/// <summary>The store tolerates a comment on read and never writes one.</summary>
/// <remarks>
/// <para>The asymmetry is the whole rule. Tolerating a comment costs nothing and makes the store
/// robust against a hand edit. Writing one costs everything for a consumer whose reader leaves
/// comment handling at disallow: such a reader does not degrade on a comment, it fails the file, and
/// the person sees a settings file that has stopped working rather than a format disagreement.</para>
/// <para>A comment the file already carried and the write did not touch stays where it is. That is
/// preservation — the bytes were copied across, not authored — and these tests separate the two.</para>
/// </remarks>
public sealed class CommentEmissionTests : SectionedTestBase
{
    private static readonly JsonReaderOptions Strict = new() { CommentHandling = JsonCommentHandling.Disallow };

    [Fact]
    public void A_write_puts_no_comment_into_a_document_that_had_none()
    {
        Given("""
            {
              "version": 1,
              "graph": { "Span": "P7D", "Points": 24 }
            }
            """);

        var store = Create();

        // Values and keys that spell comment markers: the only thing that keeps them out of the
        // document as comments is that a comment is never composed at all.
        Assert.True(store.Section<GeneralSection>("general")
            .Write(new GeneralSection { Label = "// not a comment", Groups = { ["a/*b*/c"] = true } }).Saved);
        Assert.True(store.Section<WindowSection>("window").Update(w => w.Width = 1000).Saved);

        Assert.Empty(CommentsIn(OnDiskBytes()));
        Assert.True(Parses(OnDiskBytes(), Strict));
    }

    [Fact]
    public void A_document_written_from_nothing_carries_no_comment()
    {
        var store = Create();
        Assert.True(store.Section<GeneralSection>("general").Write(new GeneralSection { Label = "/* x */" }).Saved);

        Assert.Empty(CommentsIn(OnDiskBytes()));
        Assert.True(Parses(OnDiskBytes(), Strict));
    }

    [Fact]
    public void A_write_leaves_the_documents_own_comments_exactly_as_they_were_and_adds_none()
    {
        Given("""
            {
              // the one above
              "version": 1,
              "general": {
                /* the one inside */
                "Label": "before"
              },
              "graph": { "Span": "P7D" }
            }
            """);
        var before = CommentsIn(OnDiskBytes());

        Assert.True(Create().Section<GeneralSection>("general").Update(g => g.Label = "after").Saved);

        Assert.Equal(before, CommentsIn(OnDiskBytes()));
        Assert.Contains("\"Label\": \"after\"", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_holding_nothing_but_a_comment_keeps_it_when_a_member_is_added()
    {
        Given("""
            {
              "version": 1,
              "general": {
                /* the only thing in here */
              }
            }
            """);

        Assert.True(Create().Section<CounterSection>("general").Update(c => c.Retries = 9).Saved);

        Assert.Equal([" the only thing in here "], CommentsIn(OnDiskBytes()));
        Assert.Contains("\"Retries\": 9", OnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_section_still_gains_its_members()
    {
        Given("""
            {
              "version": 1,
              "general": {}
            }
            """);

        Assert.True(Create().Section<CounterSection>("general").Update(c => c.Retries = 9).Saved);

        Assert.Contains("\"Retries\": 9", OnDisk(), StringComparison.Ordinal);
        Assert.Equal(9, Create().Section<CounterSection>("general").Read().Retries);
    }

    // Every comment in the bytes, in file order. A comment-allowing reader is the only thing that can
    // tell one from the same characters inside a string.
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
}
