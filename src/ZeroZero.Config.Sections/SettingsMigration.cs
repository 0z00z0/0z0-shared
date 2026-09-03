using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ZeroZero.Config.Sections;

/// <summary>Which top-level keys of the old document become members of which section.</summary>
/// <param name="Section">The section key the members land in.</param>
/// <param name="Keys">The old document's top-level keys that move into it. A key the old document
/// does not carry is simply absent from the result.</param>
public sealed record SettingsSectionMove(string Section, IReadOnlyList<string> Keys);

/// <summary>What one migration is asked to do.</summary>
/// <param name="SourcePath">The document to read. It is never written, renamed or deleted.</param>
/// <param name="TargetPath">The document to write. Refused if it already exists.</param>
public sealed record SettingsMigrationRequest(string SourcePath, string TargetPath)
{
    /// <summary>The version the new document declares.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The grouping. An empty list carries every key across at the top level, which is what
    /// a document that is already sectioned needs.</summary>
    public IReadOnlyList<SettingsSectionMove> Moves { get; init; } = [];
}

/// <summary>How a migration ended.</summary>
public enum SettingsMigrationOutcome
{
    /// <summary>The new document was written and read back with everything the old one carried.</summary>
    Migrated,

    /// <summary>The new document is already there. Nothing was read and nothing was written.</summary>
    TargetAlreadyExists,

    /// <summary>There is no old document, so there is nothing to carry across.</summary>
    SourceMissing,

    /// <summary>The old document could not be read. It may be perfectly intact, so nothing was
    /// written.</summary>
    SourceUnreadable,

    /// <summary>The old document is not a JSON object, so nothing can be read out of it without
    /// guessing.</summary>
    SourceNotADocument,

    /// <summary>The request is contradictory — the same path twice, or one key claimed by two
    /// sections.</summary>
    RequestRefused,

    /// <summary>The new document could not be written.</summary>
    WriteFailed,

    /// <summary>The new document was written, read back, and did not carry everything the old one
    /// did. It has been removed.</summary>
    NotProven,
}

/// <summary>What a migration did, and what it carried.</summary>
public sealed record SettingsMigrationResult(SettingsMigrationOutcome Outcome)
{
    /// <summary>Whether the new document is on disk and proven.</summary>
    public bool Migrated => Outcome == SettingsMigrationOutcome.Migrated;

    /// <summary>Every top-level key of the old document that landed in the new one, in the old
    /// document's order. The version key is not among them: it is the one value a migration
    /// replaces.</summary>
    public IReadOnlyList<string> Carried { get; init; } = [];

    /// <summary>How many comments were carried across.</summary>
    public int Comments { get; init; }

    /// <summary>Why it stopped, where an exception is what stopped it.</summary>
    public Exception? Error { get; init; }

    /// <summary>What the read-back found missing, when the outcome is
    /// <see cref="SettingsMigrationOutcome.NotProven"/>.</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];
}

/// <summary>Writes a new settings document from an old one, and leaves the old one completely
/// alone.</summary>
/// <remarks>
/// <para>The old file is opened to read and nothing else: it is never written, never renamed and
/// never deleted, not even on success. Retiring it is the application's decision, taken once it has
/// seen its own load from the new document succeed — so a migration that goes wrong costs nothing,
/// because the file it came from is still exactly where it was.</para>
/// <para>Every top-level key of the old document lands in the new one, either inside the section it
/// was mapped into or at the top level where it already was, and its value is carried as the bytes
/// the old file held rather than re-serialised through any type. Comments are carried with the key
/// they sit above. Before the migration reports success it reads the new document back off the disk
/// and checks that every key, every value and every comment arrived; if any did not, the new
/// document is removed and the failure is reported.</para>
/// </remarks>
public static class SettingsMigration
{
    /// <summary>Runs one migration. Never throws for anything the file system or the documents can
    /// do; the outcome carries the reason.</summary>
    public static SettingsMigrationResult Run(SettingsMigrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetPath);

        if (Refuse(request) is { } refusal) return refusal;

        if (File.Exists(request.TargetPath))
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.TargetAlreadyExists);
        }

        if (!File.Exists(request.SourcePath))
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.SourceMissing);
        }

        byte[] source;
        try
        {
            // Read-only, and shared, so a migration cannot be what stops the application it is
            // migrating from reading its own settings.
            using var stream = new FileStream(request.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            source = new byte[stream.Length];
            stream.ReadExactly(source);
        }
        catch (Exception ex) when (AtomicFile.IsFileFailure(ex))
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.SourceUnreadable) { Error = ex };
        }

        if (JsonObjectSpans.TryReadDocument(source) is not { } root)
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.SourceNotADocument);
        }

        // A section whose name the old document already uses would be written twice, so the
        // application is told to say what it means rather than have a merge guessed for it.
        foreach (var move in request.Moves.Where(move => root.Find(move.Section) is not null))
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.RequestRefused)
            {
                Error = new ArgumentException(
                    $"The old document already carries a top-level key '{move.Section}', so moving keys into a section of that name would write it twice."),
            };
        }

        var layout = JsonLayout.Detect(source);
        var comments = Comments(source, root);
        var plan = Plan.Build(root, request.Moves);

        var target = Compose(source, root, layout, comments, plan, request.Version);

        if (AtomicFile.Write(request.TargetPath, target) is { } failure)
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.WriteFailed) { Error = failure };
        }

        return ProveTarget(request, source);
    }

    /// <summary>Reads the new document back off the disk and checks it against the old one. The old
    /// document is walked again from scratch here rather than reusing what composing it produced, so
    /// the check is an independent reading of the same file and not a restatement of the write.</summary>
    internal static SettingsMigrationResult ProveTarget(SettingsMigrationRequest request, byte[] source)
    {
        var root = JsonObjectSpans.TryReadDocument(source)
            ?? throw new InvalidOperationException("The old document no longer parses as one.");

        return Prove(request, source, root, Comments(source, root), Plan.Build(root, request.Moves));
    }

    private static SettingsMigrationResult? Refuse(SettingsMigrationRequest request)
    {
        if (string.Equals(
                Path.GetFullPath(request.SourcePath),
                Path.GetFullPath(request.TargetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return new SettingsMigrationResult(SettingsMigrationOutcome.RequestRefused)
            {
                Error = new ArgumentException(
                    "The old and the new document are the same file, so the old one could not be left alone.",
                    nameof(request)),
            };
        }

        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var sections = new HashSet<string>(StringComparer.Ordinal);

        foreach (var move in request.Moves)
        {
            if (!sections.Add(move.Section))
            {
                return Contradiction($"Section '{move.Section}' is named by two moves.");
            }

            foreach (var key in move.Keys)
            {
                if (!claimed.Add(key)) return Contradiction($"Key '{key}' is claimed by two sections.");
            }
        }

        foreach (var section in sections)
        {
            if (claimed.Contains(section))
            {
                return Contradiction($"Key '{section}' is both a section and a key moved into one.");
            }
        }

        return null;

        static SettingsMigrationResult Contradiction(string message) =>
            new(SettingsMigrationOutcome.RequestRefused) { Error = new ArgumentException(message) };
    }

    // Every comment that is not inside a value, paired with the key it sits above — or with nothing,
    // when it sits after the last key.
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

                // A comment inside a value travels with that value's bytes, so it is not one of the
                // comments this pass has to place.
                if (root.Members.Any(member => at >= member.ValueStart && at < member.ValueEnd)) continue;

                var owner = root.Members.FirstOrDefault(member => member.NameStart > at).Name;

                // A line comment's token runs to the line break, so the text is trimmed: what has to
                // arrive in the new document is the comment, not the newline that ended it.
                var text = Encoding.UTF8.GetString(source.AsSpan(at, end - at)).TrimEnd();
                found.Add(new CommentSpan(text, owner));
            }
        }
        catch (JsonException)
        {
            // A document the structural walk accepted cannot fail here, but a comment that cannot be
            // read is one that will not be carried, and the read-back is what reports that.
        }

        return found;
    }

    private static byte[] Compose(
        byte[] source,
        JsonObjectSpan root,
        JsonLayout layout,
        List<CommentSpan> comments,
        Plan plan,
        int version)
    {
        var text = new StringBuilder();

        text.Append('{').Append(layout.NewLine)
            .Append(layout.Unit).Append('"').Append(SettingsDocument.VersionKey).Append("\": ")
            .Append(version.ToString(CultureInfo.InvariantCulture));

        foreach (var entry in plan.Entries)
        {
            text.Append(',').Append(layout.NewLine);
            Append(text, source, layout, comments, entry, layout.Unit);
        }

        foreach (var comment in comments.Where(static c => c.Owner is null))
        {
            text.Append(layout.NewLine).Append(layout.Unit).Append(comment.Text);
        }

        text.Append(layout.NewLine).Append('}').Append(layout.NewLine);

        var body = Encoding.UTF8.GetBytes(text.ToString());
        return layout.ByteOrderMark ? [.. Encoding.UTF8.GetPreamble(), .. body] : body;
    }

    private static void Append(
        StringBuilder text,
        byte[] source,
        JsonLayout layout,
        List<CommentSpan> comments,
        PlanEntry entry,
        string indent)
    {
        if (entry.Section is { } section)
        {
            text.Append(indent).Append('"').Append(JsonEncodedText.Encode(section)).Append("\": {");

            var inner = indent + layout.Unit;
            var first = true;
            foreach (var member in entry.Members)
            {
                if (!first) text.Append(',');
                text.Append(layout.NewLine);
                first = false;
                Append(text, source, layout, comments, new PlanEntry(null, [member]), inner);
            }

            text.Append(layout.NewLine).Append(indent).Append('}');
            return;
        }

        var only = entry.Members[0];
        foreach (var comment in comments.Where(c => c.Owner == only.Name))
        {
            text.Append(indent).Append(comment.Text).Append(layout.NewLine);
        }

        var value = layout.Shift(source.AsSpan(only.ValueStart..only.ValueEnd), Shift(layout, indent));

        // The name is carried as the file's own bytes, colon included, so a key holding an escape
        // sequence is written back exactly as it was rather than escaped a second time.
        text.Append(indent)
            .Append(Encoding.UTF8.GetString(source.AsSpan(only.NameStart..only.NameEnd)))
            .Append(' ')
            .Append(Encoding.UTF8.GetString(value));
    }

    // A value carried from the old document was laid out at whatever depth it sat at there; the shift
    // is the difference between that depth's indent and the one it lands at.
    private static string Shift(JsonLayout layout, string indent) =>
        indent.StartsWith(layout.Unit, StringComparison.Ordinal) ? indent[layout.Unit.Length..] : indent;

    // The new document is read back off the disk, not from the bytes just composed: what has to be
    // true is that the file carries everything, and only a read proves that.
    private static SettingsMigrationResult Prove(
        SettingsMigrationRequest request,
        byte[] source,
        JsonObjectSpan sourceRoot,
        List<CommentSpan> comments,
        Plan plan)
    {
        var missing = new List<string>();

        try
        {
            var written = File.ReadAllBytes(request.TargetPath);
            var text = Encoding.UTF8.GetString(written);

            if (JsonObjectSpans.TryReadDocument(written) is not { } root)
            {
                return Unproven(request, ["the new document does not parse"]);
            }

            if (root.Find(SettingsDocument.VersionKey) is not { } version ||
                JsonObjectSpans.Text(written, version.ValueStart, version.ValueEnd)
                    != request.Version.ToString(CultureInfo.InvariantCulture))
            {
                missing.Add("version");
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

            foreach (var comment in comments.Where(comment => !text.Contains(comment.Text, StringComparison.Ordinal)))
            {
                missing.Add(comment.Text);
            }
        }
        catch (Exception ex) when (AtomicFile.IsFileFailure(ex))
        {
            return Unproven(request, ["the new document could not be read back"], ex);
        }

        if (missing.Count > 0) return Unproven(request, missing);

        return new SettingsMigrationResult(SettingsMigrationOutcome.Migrated)
        {
            Carried = [.. sourceRoot.Members.Select(static m => m.Name).Where(static n => n != SettingsDocument.VersionKey)],
            Comments = comments.Count,
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

    private readonly record struct CommentSpan(string Text, string? Owner);

    private readonly record struct PlanEntry(string? Section, IReadOnlyList<JsonMemberSpan> Members);

    // What the new document's top level holds, in the old document's own order: a section takes the
    // place of the first key that moves into it, and a key that moves nowhere stays where it was.
    private sealed class Plan
    {
        private readonly Dictionary<string, string> _into;

        private Plan(List<PlanEntry> entries, Dictionary<string, string> into)
        {
            Entries = entries;
            _into = into;
        }

        internal List<PlanEntry> Entries { get; }

        internal static Plan Build(JsonObjectSpan root, IReadOnlyList<SettingsSectionMove> moves)
        {
            var into = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var move in moves)
            {
                foreach (var key in move.Keys) into[key] = move.Section;
            }

            var entries = new List<PlanEntry>();
            var sections = new Dictionary<string, List<JsonMemberSpan>>(StringComparer.Ordinal);

            foreach (var member in root.Members)
            {
                if (member.Name == SettingsDocument.VersionKey) continue;

                if (!into.TryGetValue(member.Name, out var section))
                {
                    entries.Add(new PlanEntry(null, [member]));
                    continue;
                }

                if (!sections.TryGetValue(section, out var members))
                {
                    members = [];
                    sections[section] = members;
                    entries.Add(new PlanEntry(section, members));
                }

                members.Add(member);
            }

            return new Plan(entries, into);
        }

        internal string? SectionOf(string key) => _into.GetValueOrDefault(key);
    }
}
