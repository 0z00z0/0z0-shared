using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ZeroZero.Config.Sections;

/// <summary>What reading one section of a document found.</summary>
internal enum SectionOutcome
{
    /// <summary>The document carries no section of that name.</summary>
    Missing,

    /// <summary>The section was read into the type.</summary>
    Bound,

    /// <summary>The section is there but this build cannot read it — a value of the wrong kind, or an
    /// enum member no type answers to. Its bytes stay in the file untouched.</summary>
    Unparseable,
}

/// <summary>A sectioned settings document, held as the bytes that are on disk.</summary>
/// <remarks>
/// <para>Nothing here binds the document to a type. Sections are located as byte ranges, one section
/// is bound at a time, and a write replaces the byte ranges of the values it changes and nothing
/// else — so a sibling section, a section from a build that no longer exists, a comment and the
/// file's own key order all survive because they are never handled, not because a rule says they
/// should be.</para>
/// <para>The <c>version</c> key comes first and is written only when the document has none. Raising
/// an existing version is refused here on purpose: sections belong to independently released
/// components, so declaring that the whole document has moved to a new shape is a decision above any
/// one section, and it belongs to the migration.</para>
/// </remarks>
internal sealed class SettingsDocument
{
    internal const string VersionKey = "version";

    private readonly byte[] _content;
    private readonly JsonObjectSpan _root;

    private SettingsDocument(byte[] content, JsonObjectSpan root, JsonLayout layout)
    {
        _content = content;
        _root = root;
        Layout = layout;
    }

    /// <summary>The document exactly as it is on disk.</summary>
    internal byte[] Content => _content;

    internal JsonLayout Layout { get; }

    /// <summary>The declared document version, or null when the document carries no whole-number
    /// <c>version</c> key — the older, flat shape.</summary>
    internal int? Version
    {
        get
        {
            if (_root.Find(VersionKey) is not { } member) return null;

            var text = JsonObjectSpans.Text(_content, member.ValueStart, member.ValueEnd);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                ? version
                : null;
        }
    }

    /// <summary>Every top-level key, in file order, the version key included.</summary>
    internal IReadOnlyList<string> Keys => [.. _root.Members.Select(static m => m.Name)];

    /// <summary>Reads the document's bytes. Null when they are not a JSON object, which is the one
    /// state a section store cannot work with.</summary>
    internal static SettingsDocument? TryParse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var root = JsonObjectSpans.TryReadDocument(content);
        return root is null ? null : new SettingsDocument(content, root, JsonLayout.Detect(content));
    }

    /// <summary>An empty document in the given layout, which is what a missing file stands for.</summary>
    internal static SettingsDocument Empty(JsonLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var content = Encoding.UTF8.GetBytes(layout.ByteOrderMark ? "﻿{}" : "{}");
        var root = JsonObjectSpans.TryReadDocument(content)
            ?? throw new InvalidOperationException("An empty object failed to parse as one.");

        return new SettingsDocument(content, root, layout);
    }

    /// <summary>The bytes one section occupies, or nothing when the document has no such key. Used to
    /// tell whether a reload moved a section without binding it to anything.</summary>
    internal ReadOnlySpan<byte> SectionContent(string name) =>
        _root.Find(name) is { } member ? _content.AsSpan(member.ValueStart..member.ValueEnd) : default;

    /// <summary>Binds one section, and only that section, to <typeparamref name="T"/>.</summary>
    internal SectionOutcome TryReadSection<T>(string name, JsonSerializerOptions serialiser, out T value)
        where T : class, new()
    {
        if (_root.Find(name) is not { } member)
        {
            value = new T();
            return SectionOutcome.Missing;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(_content.AsSpan(member.ValueStart..member.ValueEnd), serialiser)
                ?? new T();
            return SectionOutcome.Bound;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            value = new T();
            return SectionOutcome.Unparseable;
        }
    }

    /// <summary>Rewrites one section, leaving every other byte of the document alone. Returns null
    /// when the document already says what <paramref name="value"/> says.</summary>
    /// <param name="order">The order sections take when one is added that the document lacks. A name
    /// the list does not carry is appended last.</param>
    /// <param name="documentVersion">Written as the first key when the document has no version.</param>
    internal byte[]? WriteSection<T>(
        string name,
        T value,
        JsonSerializerOptions serialiser,
        IReadOnlyList<string> order,
        int documentVersion)
        where T : class
    {
        var writer = Layout.Writer(serialiser);
        var draftBytes = JsonSerializer.SerializeToUtf8Bytes(value, writer);
        var draft = JsonObjectSpans.TryReadDocument(draftBytes)
            ?? throw new InvalidOperationException(
                $"The type behind section '{name}' does not serialise to a JSON object, so it cannot be a section.");

        var edits = new List<JsonEdit>();

        if (_root.Find(name) is not { } member) edits.Add(AddSection(name, draftBytes, order));
        else if (JsonObjectSpans.TryRead(_content, member.ValueStart..member.ValueEnd) is not { } section)
        {
            // The key is there but does not hold an object, so there is no member to preserve: the
            // store owns this section and replaces what stands in its place.
            var extra = Extra(IndentOf(member.NameStart));
            edits.Add(new JsonEdit(member.ValueStart, member.ValueEnd, Layout.Shift(draftBytes, extra)));
        }
        else edits.AddRange(RewriteMembers(section, draft, draftBytes, member, serialiser));

        if (edits.Count == 0) return null;

        // Stamped last but listed first, because whether it needs a trailing comma depends on
        // whether this same write is adding the document's only other key.
        if (_root.Find(VersionKey) is null) edits.Insert(0, StampVersion(documentVersion));

        return JsonSplice.Apply(_content, edits);
    }

    private IEnumerable<JsonEdit> RewriteMembers(
        JsonObjectSpan section,
        JsonObjectSpan draft,
        byte[] draftBytes,
        JsonMemberSpan sectionMember,
        JsonSerializerOptions serialiser)
    {
        // A member is matched the way the reader matches it. Reading case-insensitively while
        // writing the declared spelling would leave the file carrying both, and the last of two
        // keys differing only in case is the one a read then takes (measured) — so the older one
        // becomes dead weight that a hand edit silently fails to change.
        var comparison = serialiser.PropertyNameCaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var edits = new List<JsonEdit>();
        var additions = new List<(string Name, byte[] Value)>();

        foreach (var member in draft.Members)
        {
            var replacement = draftBytes.AsSpan(member.ValueStart..member.ValueEnd);

            if (section.Find(member.Name, comparison) is not { } existing)
            {
                additions.Add((member.Name, replacement.ToArray()));
                continue;
            }

            var shifted = Layout.Shift(replacement, Extra(IndentOf(existing.NameStart)));
            if (shifted.AsSpan().SequenceEqual(_content.AsSpan(existing.ValueStart..existing.ValueEnd))) continue;

            edits.Add(new JsonEdit(existing.ValueStart, existing.ValueEnd, shifted));
        }

        if (additions.Count > 0) edits.Add(AddMembers(section, sectionMember, additions));
        return edits;
    }

    // A member the file does not carry yet is appended inside the section's braces, in the file's own
    // indent, so a settings type that gained a property does not reformat the file it lands in.
    private JsonEdit AddMembers(
        JsonObjectSpan section,
        JsonMemberSpan sectionMember,
        List<(string Name, byte[] Value)> additions)
    {
        var sectionIndent = IndentOf(sectionMember.NameStart);
        var last = section.Members.Count > 0 ? section.Members[^1] : (JsonMemberSpan?)null;
        var memberIndent = last is { } anchor ? IndentOf(anchor.NameStart) : sectionIndent + Layout.Unit;

        var text = new StringBuilder();
        var inline = memberIndent.Length == 0;

        foreach (var (name, value) in additions)
        {
            text.Append(',');
            if (inline) text.Append(' ');
            else text.Append(Layout.NewLine).Append(memberIndent);

            text.Append('"').Append(JsonEncodedText.Encode(name)).Append("\": ")
                .Append(Encoding.UTF8.GetString(Layout.Shift(value, Extra(memberIndent))));
        }

        if (last is { } member) return JsonEdit.Insert(member.ValueEnd, Encoding.UTF8.GetBytes(text.ToString()));

        // An empty section has no member to hang a comma off, so the leading comma goes and the
        // closing brace gains the line it needs.
        text.Remove(0, inline ? 2 : 1);
        if (!inline) text.Append(Layout.NewLine).Append(sectionIndent);
        return new JsonEdit(section.Start + 1, section.CloseBrace, Encoding.UTF8.GetBytes(text.ToString()));
    }

    // A section the document has never carried takes the slot the declared order gives it, before the
    // first declared section already present that follows it. That is the only place order is
    // consulted: a section already in the file keeps whatever position the file gives it.
    private JsonEdit AddSection(string name, byte[] draftBytes, IReadOnlyList<string> order)
    {
        var body = Encoding.UTF8.GetString(Layout.Shift(draftBytes, Layout.Unit));
        var declared = IndexIn(order, name);

        JsonMemberSpan? follower = null;

        // A name the order does not carry has no slot to claim, so it goes after everything.
        if (declared >= 0)
        {
            foreach (var member in _root.Members)
            {
                var at = IndexIn(order, member.Name);
                if (at < 0 || at <= declared) continue;

                follower = member;
                break;
            }
        }

        if (follower is { } next)
        {
            var indent = IndentOf(next.NameStart);
            var text = $"\"{JsonEncodedText.Encode(name)}\": {body},{Layout.NewLine}{indent}";
            return JsonEdit.Insert(next.NameStart, Encoding.UTF8.GetBytes(text));
        }

        if (_root.Members.Count == 0)
        {
            var only = $"{Layout.NewLine}{Layout.Unit}\"{JsonEncodedText.Encode(name)}\": {body}{Layout.NewLine}";
            return new JsonEdit(_root.Start + 1, _root.CloseBrace, Encoding.UTF8.GetBytes(only));
        }

        var last = _root.Members[^1];
        var lastIndent = IndentOf(last.NameStart);
        var appended = lastIndent.Length == 0
            ? $", \"{JsonEncodedText.Encode(name)}\": {body}"
            : $",{Layout.NewLine}{lastIndent}\"{JsonEncodedText.Encode(name)}\": {body}";

        return JsonEdit.Insert(last.ValueEnd, Encoding.UTF8.GetBytes(appended));
    }

    // Only ever stamped alongside a section edit, so a key always follows it and the comma always
    // belongs.
    private JsonEdit StampVersion(int version)
    {
        var text = $"{Layout.NewLine}{Layout.Unit}\"{VersionKey}\": {version.ToString(CultureInfo.InvariantCulture)},";
        return JsonEdit.Insert(_root.Start + 1, Encoding.UTF8.GetBytes(text));
    }

    // The whitespace a member's own line begins with, or nothing when the member shares a line with
    // what came before it.
    private string IndentOf(int nameStart)
    {
        var at = nameStart;
        while (at > 0 && _content[at - 1] is (byte)' ' or (byte)'\t') at--;

        return at > 0 && _content[at - 1] == (byte)'\n'
            ? Encoding.UTF8.GetString(_content.AsSpan(at, nameStart - at))
            : string.Empty;
    }

    // A value serialised on its own sits one level in; moving it to a member indented further needs
    // the difference added to every line after the first.
    private string Extra(string indent) =>
        indent.StartsWith(Layout.Unit, StringComparison.Ordinal) ? indent[Layout.Unit.Length..] : indent;

    private static int IndexIn(IReadOnlyList<string> order, string name)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], name, StringComparison.Ordinal)) return i;
        }

        return -1;
    }
}
