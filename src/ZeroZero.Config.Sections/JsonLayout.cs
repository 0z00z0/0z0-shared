using System.Text;
using System.Text.Json;

namespace ZeroZero.Config.Sections;

/// <summary>How a file on disk is laid out — byte-order mark, line ending and indent — so anything
/// added to it reads as though the same hand wrote it.</summary>
/// <remarks>The layout is read off the file rather than declared, because the file may have been
/// hand-edited or written by an earlier build, and a store that reformats what it did not change is
/// a store whose diffs cannot be read.</remarks>
internal sealed class JsonLayout
{
    private JsonLayout(bool byteOrderMark, string newLine, char indentCharacter, int indentSize)
    {
        ByteOrderMark = byteOrderMark;
        NewLine = newLine;
        IndentCharacter = indentCharacter;
        IndentSize = indentSize;
        Unit = new string(indentCharacter, indentSize);
    }

    /// <summary>The layout a file that does not exist yet is written in.</summary>
    internal static JsonLayout Default { get; } = new(false, Environment.NewLine, ' ', 2);

    internal bool ByteOrderMark { get; }

    internal string NewLine { get; }

    internal char IndentCharacter { get; }

    internal int IndentSize { get; }

    /// <summary>One level of indentation.</summary>
    internal string Unit { get; }

    /// <summary>Reads the layout off a document's bytes. Anything it cannot see — a file with no line
    /// break has no indent to read — falls back to the default.</summary>
    internal static JsonLayout Detect(ReadOnlySpan<byte> content)
    {
        var bom = JsonObjectSpans.StartsWithBom(content);
        var newLine = Environment.NewLine;
        var character = ' ';
        var size = 2;

        var breakAt = content.IndexOf((byte)'\n');
        if (breakAt >= 0)
        {
            newLine = breakAt > 0 && content[breakAt - 1] == (byte)'\r' ? "\r\n" : "\n";

            var run = 0;
            var at = breakAt + 1;
            while (at + run < content.Length && content[at + run] == content[at]) run++;

            if (run > 0 && content[at] is (byte)' ' or (byte)'\t')
            {
                character = (char)content[at];
                size = run;
            }
        }

        return new JsonLayout(bom, newLine, character, size);
    }

    /// <summary>Serialiser settings that write in this file's own line ending and indent.</summary>
    internal JsonSerializerOptions Writer(JsonSerializerOptions serialiser) =>
        new(serialiser)
        {
            WriteIndented = true,
            NewLine = NewLine,
            IndentCharacter = IndentCharacter,
            IndentSize = IndentSize,
        };

    /// <summary>Pushes every line after the first across by <paramref name="extra"/>, which is how a
    /// value serialised at one depth is placed at another without being re-serialised. A line break
    /// inside a JSON string is escaped, so the only line breaks here are layout.</summary>
    internal byte[] Shift(ReadOnlySpan<byte> value, string extra)
    {
        if (extra.Length == 0) return value.ToArray();

        var text = Encoding.UTF8.GetString(value);
        return Encoding.UTF8.GetBytes(text.Replace(NewLine, NewLine + extra, StringComparison.Ordinal));
    }
}
