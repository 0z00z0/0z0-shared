using System.Text;
using System.Text.Json;

namespace ZeroZero.Config.Sections;

/// <summary>Where one member of a JSON object sits in the document's bytes.</summary>
/// <param name="Name">The member's name, unescaped.</param>
/// <param name="NameStart">Index of the opening quote of the name.</param>
/// <param name="NameEnd">Index one past the colon that follows the name, so the raw name may be
/// carried across without being escaped again — a key holding a quote or a line break is written
/// back exactly as the file had it.</param>
/// <param name="ValueStart">Index of the value's first byte.</param>
/// <param name="ValueEnd">Index one past the value's last byte.</param>
internal readonly record struct JsonMemberSpan(string Name, int NameStart, int NameEnd, int ValueStart, int ValueEnd);

/// <summary>One JSON object located in a document's bytes: its members in file order, and where its
/// braces are.</summary>
internal sealed class JsonObjectSpan
{
    internal JsonObjectSpan(int start, int closeBrace, IReadOnlyList<JsonMemberSpan> members)
    {
        Start = start;
        CloseBrace = closeBrace;
        Members = members;
    }

    /// <summary>Index of the opening brace.</summary>
    internal int Start { get; }

    /// <summary>Index of the closing brace.</summary>
    internal int CloseBrace { get; }

    /// <summary>Every member, in the order the file carries them. A name that appears twice appears
    /// twice here.</summary>
    internal IReadOnlyList<JsonMemberSpan> Members { get; }

    /// <summary>The member a deserialiser would bind: the last of that name, because that is the one
    /// <see cref="JsonSerializer"/> takes when a hand edit has left two (measured — it does not
    /// complain, it takes the last).</summary>
    internal JsonMemberSpan? Find(string name, StringComparison comparison = StringComparison.Ordinal)
    {
        for (var i = Members.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Members[i].Name, name, comparison)) return Members[i];
        }

        return null;
    }

    /// <summary>Every member of that name, in file order. A hand edit that left two of a key leaves
    /// two here, and a migration has to carry both.</summary>
    internal List<JsonMemberSpan> All(string name) =>
        [.. Members.Where(member => string.Equals(member.Name, name, StringComparison.Ordinal))];
}

/// <summary>Locates the members of a JSON object inside a document's bytes without binding any value
/// to any type.</summary>
/// <remarks>
/// <para>This is what lets a store address one section: the walk reads structure only, so a value no
/// type in this build could accept is still walked past, and its bytes are still known. Reading into
/// the object and skipping only nested containers is load-bearing — <c>Skip</c> called while
/// positioned on the object itself consumes the whole of it and leaves nothing to walk (measured).</para>
/// <para>A member's span runs from the first byte of its value to the last, so a comment beside a
/// member lies outside every span and a comment written <i>inside</i> a container value lies within
/// that value's own. Replacing a scalar's bytes therefore cannot disturb a comment; replacing a
/// whole object would, which is why a section that holds an object has its members rewritten one at
/// a time and a new member inserted rather than the braces refilled. The migration classifies a
/// comment the same way, by whether it falls inside a carried value's span.</para>
/// </remarks>
internal static class JsonObjectSpans
{
    /// <summary>Reads with the tolerance a hand-edited file needs: comments skipped, a trailing comma
    /// accepted.</summary>
    internal static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    /// <summary>The bytes a UTF-8 byte-order mark occupies.</summary>
    internal const int BomLength = 3;

    /// <summary>Whether the content opens with a UTF-8 byte-order mark, which the reader rejects as
    /// an invalid start of a value and which therefore has to be set aside before the walk and put
    /// back on the write.</summary>
    internal static bool StartsWithBom(ReadOnlySpan<byte> content) =>
        content.Length >= BomLength && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF;

    /// <summary>Walks the object occupying <paramref name="range"/> of <paramref name="content"/>.
    /// Returns null when those bytes are not a well-formed JSON object.</summary>
    internal static JsonObjectSpan? TryRead(ReadOnlySpan<byte> content, Range range)
    {
        var (offset, length) = range.GetOffsetAndLength(content.Length);
        var slice = content.Slice(offset, length);

        try
        {
            var reader = new Utf8JsonReader(slice, ReaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

            var start = offset + (int)reader.TokenStartIndex;
            var members = new List<JsonMemberSpan>();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) return null;

                var nameStart = offset + (int)reader.TokenStartIndex;
                var name = reader.GetString();
                if (name is null) return null;

                var nameEnd = offset + (int)reader.BytesConsumed;

                if (!reader.Read()) return null;
                var valueStart = offset + (int)reader.TokenStartIndex;

                // Only a container needs skipping past; skipping while positioned on the object
                // being walked would consume the whole object instead of one member.
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();

                members.Add(new JsonMemberSpan(name, nameStart, nameEnd, valueStart, offset + (int)reader.BytesConsumed));
            }

            if (reader.TokenType != JsonTokenType.EndObject) return null;

            return new JsonObjectSpan(start, offset + (int)reader.TokenStartIndex, members);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Walks the whole document as one object, past any byte-order mark.</summary>
    internal static JsonObjectSpan? TryReadDocument(ReadOnlySpan<byte> content)
    {
        var start = StartsWithBom(content) ? BomLength : 0;
        return TryRead(content, start..content.Length);
    }

    /// <summary>Whether the bytes hold nothing but whitespace, which states no settings at all and so
    /// carries nothing worth preserving.</summary>
    internal static bool IsBlank(ReadOnlySpan<byte> content)
    {
        var start = StartsWithBom(content) ? BomLength : 0;
        for (var i = start; i < content.Length; i++)
        {
            if (content[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) return false;
        }

        return true;
    }

    /// <summary>The text of a byte range, for a caller that needs to look at a value rather than move
    /// it.</summary>
    internal static string Text(ReadOnlySpan<byte> content, int start, int end) =>
        Encoding.UTF8.GetString(content[start..end]);
}
