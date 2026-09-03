namespace ZeroZero.Config.Sections;

/// <summary>One byte range of a document to be replaced. An insertion is a range of zero length.</summary>
internal readonly record struct JsonEdit(int Start, int End, byte[] Replacement)
{
    internal static JsonEdit Insert(int at, byte[] text) => new(at, at, text);
}

/// <summary>Rewrites a document by replacing byte ranges of it.</summary>
/// <remarks>This is the whole of how a section write works, and the reason a sibling section cannot
/// be lost: every byte the edits do not name is copied across unchanged, so preserving the rest of
/// the file is a property of the mechanism rather than a rule the code has to remember.</remarks>
internal static class JsonSplice
{
    /// <summary>Applies every edit to <paramref name="content"/>. Edits may arrive in any order and
    /// must not overlap; two insertions at the same point keep the order they were listed in, which
    /// is what puts a stamped version key before a section added in the same write.</summary>
    internal static byte[] Apply(ReadOnlySpan<byte> content, List<JsonEdit> edits)
    {
        if (edits.Count == 0) return content.ToArray();

        var listed = edits.Index().ToList();
        listed.Sort(static (a, b) =>
            a.Item.Start != b.Item.Start ? a.Item.Start.CompareTo(b.Item.Start)
            : a.Item.End != b.Item.End ? a.Item.End.CompareTo(b.Item.End)
            : a.Index.CompareTo(b.Index));

        var ordered = listed.Select(static pair => pair.Item).ToList();

        var size = content.Length;
        var previousEnd = 0;
        foreach (var edit in ordered)
        {
            if (edit.Start < previousEnd)
            {
                throw new InvalidOperationException(
                    "Two edits cover the same bytes, so one would silently discard the other.");
            }

            previousEnd = edit.End;
            size += edit.Replacement.Length - (edit.End - edit.Start);
        }

        var result = new byte[size];
        var read = 0;
        var written = 0;

        foreach (var edit in ordered)
        {
            var run = edit.Start - read;
            content.Slice(read, run).CopyTo(result.AsSpan(written));
            written += run;

            edit.Replacement.CopyTo(result.AsSpan(written));
            written += edit.Replacement.Length;

            read = edit.End;
        }

        content[read..].CopyTo(result.AsSpan(written));
        return result;
    }
}
