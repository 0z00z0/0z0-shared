using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ZeroZero.Config.Sections;

/// <summary>The read-back that proves a migration before it reports success.</summary>
internal static class MigrationProof
{
    /// <summary>Reads the new document back off the disk and checks it against the old one. The old
    /// document is walked again from scratch here rather than reusing what composing it produced, so
    /// the check is an independent reading of the same file and not a restatement of the write.</summary>
    internal static SettingsMigrationResult ProveTarget(SettingsMigrationRequest request, byte[] source)
    {
        var root = JsonObjectSpans.TryReadDocument(source)
            ?? throw new InvalidOperationException("The old document no longer parses as one.");

        return Prove(request, source, root, Comments(source, root), MigrationPlan.Build(root, request.Moves));
    }

    // Every comment in the document, each marked by whether it sits inside a value. A comment inside a
    // value travels with that value's bytes; one outside is not written at all.
    private static List<CommentSpan> Comments(byte[] source, JsonObjectSpan root)
    {
        var options = new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Allow,
            AllowTrailingCommas = true,
            MaxDepth = JsonObjectSpans.ReaderOptions.MaxDepth,
        };

        var start = JsonObjectSpans.StartsWithBom(source) ? JsonObjectSpans.BomLength : 0;
        var found = new List<CommentSpan>();

        try
        {
            var reader = new Utf8JsonReader(source.AsSpan(start), options);
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.Comment) continue;

                var at = start + (int)reader.TokenStartIndex;
                var end = start + (int)reader.BytesConsumed;

                var inside = root.Members.Any(member => at >= member.ValueStart && at < member.ValueEnd);

                // A line comment's token runs to the line break, so the text is trimmed: the comment
                // is what is being counted, not the newline that ended it.
                var text = Encoding.UTF8.GetString(source.AsSpan(at, end - at)).TrimEnd();
                found.Add(new CommentSpan(text, inside));
            }
        }
        catch (JsonException)
        {
            // A document the structural walk accepted cannot fail here, but a comment that cannot be
            // read is one that will not be carried, and the read-back is what reports that.
        }

        return found;
    }

    // The new document is read back off the disk, not from the bytes just composed: what has to be
    // true is that the file carries everything, and only a read proves that.
    private static SettingsMigrationResult Prove(
        SettingsMigrationRequest request,
        byte[] source,
        JsonObjectSpan sourceRoot,
        List<CommentSpan> comments,
        MigrationPlan plan)
    {
        var missing = new List<string>();

        try
        {
            var written = File.ReadAllBytes(request.TargetPath);

            if (JsonObjectSpans.TryReadDocument(written) is not { } root)
            {
                return Unproven(request, ["the new document does not parse"]);
            }

            if (root.Find(SettingsDocument.VersionKey) is not { } version ||
                JsonObjectSpans.Text(written, version.ValueStart, version.ValueEnd)
                    != request.Version.ToString(CultureInfo.InvariantCulture))
            {
                missing.Add(SettingsDocument.VersionKey);
            }

            // Counted by occurrence, because a hand edit that left a key twice must land twice: the
            // n-th of a name in the old document is checked against the n-th in the new one.
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var member in sourceRoot.Members)
            {
                if (member.Name == SettingsDocument.VersionKey) continue;

                var into = plan.SectionOf(member.Name);
                var holder = into is null ? root : JsonObjectSpans.TryRead(written, Range(root.Find(into)));

                seen.TryGetValue(member.Name, out var index);
                seen[member.Name] = index + 1;

                var landed = holder?.All(member.Name);
                if (landed is null || landed.Count <= index)
                {
                    missing.Add(member.Name);
                    continue;
                }

                if (!Equivalent(source, member, written, landed[index])) missing.Add(member.Name);
            }

            // Exactly the comments a carried value brought with it, and no others: any other is one the
            // migration authored.
            var expected = comments.Where(static comment => comment.InsideValue).Select(static comment => comment.Text);
            var found = Comments(written, root).Select(static comment => comment.Text).ToList();

            foreach (var comment in expected)
            {
                if (!found.Remove(comment)) missing.Add(comment);
            }

            foreach (var extra in found) missing.Add($"a comment the old document did not have: {extra}");
        }
        catch (Exception ex) when (AtomicFile.IsFileFailure(ex))
        {
            return Unproven(request, ["the new document could not be read back"], ex);
        }

        if (missing.Count > 0) return Unproven(request, missing);

        return new SettingsMigrationResult(SettingsMigrationOutcome.Migrated)
        {
            Carried = [.. sourceRoot.Members.Select(static m => m.Name).Where(static n => n != SettingsDocument.VersionKey)],
            CommentsNotCarried = [.. comments.Where(static c => !c.InsideValue).Select(static c => c.Text)],
            CommentsInsideValues = [.. comments.Where(static c => c.InsideValue).Select(static c => c.Text)],
        };
    }

    // A value is the same value however it is laid out, so the comparison ignores the whitespace
    // between tokens and nothing else.
    private static bool Equivalent(byte[] source, JsonMemberSpan from, byte[] written, JsonMemberSpan to) =>
        Tokens(source.AsSpan(from.ValueStart..from.ValueEnd)) == Tokens(written.AsSpan(to.ValueStart..to.ValueEnd));

    private static string Tokens(ReadOnlySpan<byte> value)
    {
        var text = new StringBuilder();
        var reader = new Utf8JsonReader(value, JsonObjectSpans.ReaderOptions);

        while (reader.Read())
        {
            text.Append(reader.TokenType).Append(':').Append(Encoding.UTF8.GetString(reader.ValueSpan)).Append('|');
        }

        return text.ToString();
    }

    private static Range Range(JsonMemberSpan? member) =>
        member is { } found ? found.ValueStart..found.ValueEnd : 0..0;

    private static SettingsMigrationResult Unproven(
        SettingsMigrationRequest request,
        IReadOnlyList<string> missing,
        Exception? error = null)
    {
        // The new document is the one this run created, so removing it leaves the disk as it was; the
        // old document has not been touched at any point.
        AtomicFile.TryDelete(request.TargetPath);

        return new SettingsMigrationResult(SettingsMigrationOutcome.NotProven) { Missing = missing, Error = error };
    }

    private readonly record struct CommentSpan(string Text, bool InsideValue);
}
