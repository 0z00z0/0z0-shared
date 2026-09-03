using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZeroZero.Config.Watch;

/// <summary>Decides whether one settings state differs from another in a way that matters.</summary>
/// <remarks>
/// <para>Both states are serialised, a named list of cosmetic values is removed from each, and what
/// remains is compared. <b>Everything the list does not name is compared.</b> That direction is the
/// whole point: a property added later is weighed from the moment it exists, and a property is only
/// ever skipped because somebody wrote its name down. The opposite direction — an inclusion list —
/// makes every new property invisible until somebody remembers to add it, which is a reload that
/// silently stops firing.</para>
/// <para>The mechanism is general; the question is not. One store may be watched by two classifiers
/// asking different questions of the same file, so each carries the <see cref="Question"/> it
/// answers and the answer means nothing without it.</para>
/// </remarks>
/// <typeparam name="T">The settings shape being compared.</typeparam>
public sealed class SettingsChangeClassifier<T> where T : class, new()
{
    /// <summary>Separates the segments of a path naming something nested.</summary>
    public const char PathSeparator = '/';

    private readonly JsonSerializerOptions _serialiser;
    private readonly string[][] _cosmetic;

    /// <summary>Builds a classifier for one question.</summary>
    /// <param name="question">What the answer means to the application asking — "must the monitor
    /// re-evaluate?", not "did anything change?". Carried on every result.</param>
    /// <param name="cosmetic">The values a change to which does not count. A name on its own is a
    /// property of the settings shape; <c>window/left</c> names one inside a nested object, and
    /// <c>window</c> on its own skips that whole object. Matching ignores case, because the file is
    /// read that way. An empty list means every difference counts.</param>
    /// <param name="serialiser">How both states are serialised. Defaults to the settings default, so
    /// what is compared is what reaches the file.</param>
    /// <exception cref="ArgumentException">A path whose first segment names nothing the settings
    /// shape serialises — a misspelling, or a member renamed out from under the list. It is reported
    /// rather than ignored because a skip that quietly stops applying is invisible until somebody
    /// wonders why the application reacts to a window being moved.</exception>
    public SettingsChangeClassifier(
        string question,
        IEnumerable<string> cosmetic,
        JsonSerializerOptions? serialiser = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(cosmetic);

        Question = question;
        _serialiser = serialiser ?? SettingsFileOptions.DefaultSerialiser;

        var paths = cosmetic.ToArray();
        foreach (var path in paths) ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(cosmetic));

        Cosmetic = paths;
        _cosmetic = [.. paths.Select(path => path.Split(PathSeparator, StringSplitOptions.TrimEntries))];

        Verify();
    }

    /// <summary>What this classifier's answer means to the application asking.</summary>
    public string Question { get; }

    /// <summary>The values a change to which does not count, as given.</summary>
    public IReadOnlyList<string> Cosmetic { get; }

    /// <summary>Everything about <paramref name="value"/> that this question cares about, as one
    /// string. Two states with equal fingerprints differ only cosmetically.</summary>
    public string Fingerprint(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var node = JsonSerializer.SerializeToNode(value, _serialiser);
        if (node is not JsonObject root) return node?.ToJsonString() ?? "null";

        foreach (var path in _cosmetic) Remove(root, path);
        return root.ToJsonString();
    }

    /// <summary>Whether the difference between two states is one this question cares about.</summary>
    public bool IsSubstantive(T before, T after) =>
        !string.Equals(Fingerprint(before), Fingerprint(after), StringComparison.Ordinal);

    // Only the first segment is checked. A deeper segment may legitimately name nothing in a default
    // instance — a nested object that is null until the application fills it in, an entry of a
    // dictionary whose keys the type does not declare — so demanding it would refuse correct lists.
    private void Verify()
    {
        if (_cosmetic.Length == 0) return;

        if (JsonSerializer.SerializeToNode(new T(), _serialiser) is not JsonObject root)
        {
            throw new InvalidOperationException(
                $"The type behind the question '{Question}' does not serialise to a JSON object, so nothing about it can be named cosmetic.");
        }

        foreach (var path in _cosmetic)
        {
            if (Find(root, path[0]) is not null) continue;

            throw new ArgumentException(
                $"'{path[0]}' names nothing the settings shape serialises, so the cosmetic entry '{string.Join(PathSeparator, path)}' would never apply.",
                nameof(Cosmetic));
        }
    }

    private static void Remove(JsonObject root, string[] path)
    {
        var parent = root;

        for (var depth = 0; depth < path.Length - 1; depth++)
        {
            if (Find(parent, path[depth]) is not { } key) return;
            if (parent[key] is not JsonObject next) return;
            parent = next;
        }

        if (Find(parent, path[^1]) is { } last) parent.Remove(last);
    }

    // The key as the document spells it, or null. Scanned rather than indexed because the indexer's
    // case sensitivity follows the options the node was built with, and this must not.
    private static string? Find(JsonObject parent, string name)
    {
        foreach (var pair in parent)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        }

        return null;
    }
}
