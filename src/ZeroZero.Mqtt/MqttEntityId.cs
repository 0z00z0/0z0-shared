using System.Text;

namespace ZeroZero.Mqtt;

/// <summary>The entity id: the same alphabet <see cref="MqttIdentity"/> gives a device id, plus the
/// collision resolution an id composed at runtime needs. Pure.</summary>
/// <remarks>
/// <para>An entity id is a topic segment, the <c>unique_id</c> stem after the device id, and the
/// suffix a command is addressed to. Two entities sharing one would route the first's commands to
/// the second, so a collision is a correctness failure rather than a cosmetic one — and ids composed
/// from runtime names collide easily, because the names they come from differ in exactly the
/// characters this alphabet drops.</para>
/// <para>Runs of rejected characters collapse to one underscore and the ends are trimmed, so a name
/// like "Web server (2)" reads as <c>web_server_2</c>. That differs from
/// <see cref="MqttIdentity.Normalise"/> on purpose: a device id is already carried by every retained
/// topic on every existing installation, and an entity id composed at runtime is not.</para>
/// </remarks>
public static class MqttEntityId
{
    /// <summary>Longest entity id produced, suffix included — keeps the id readable inside a topic
    /// path that already carries a root and a device id.</summary>
    public const int MaxLength = 48;

    /// <summary>What an id reduces to when nothing usable survives — a hand-edited name of only
    /// punctuation, or an empty one.</summary>
    public const string Fallback = "entity";

    /// <summary>Lower-cases and reduces to [a-z0-9_], collapsing runs and trimming the ends.</summary>
    public static string Normalise(string? raw)
    {
        var sb = new StringBuilder((raw ?? "").Length);
        bool pendingSeparator = false;

        foreach (char c in (raw ?? "").ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingSeparator && sb.Length > 0) sb.Append('_');
                pendingSeparator = false;
                sb.Append(c);
            }
            else pendingSeparator = true;
        }

        string text = sb.ToString();
        if (text.Length == 0) return Fallback;
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }

    /// <summary>Why an id is unusable, or null when it is fine.</summary>
    public static string? Validate(string? raw) =>
        Normalise(raw) == Fallback && !string.Equals(raw?.Trim(), Fallback, StringComparison.OrdinalIgnoreCase)
            ? "An entity id must contain at least one letter or digit."
            : null;

    /// <summary>Normalises a whole list at once, resolving collisions in input order: the first
    /// claimant keeps the plain id and each later one takes the next free numeric suffix.</summary>
    /// <remarks>Order matters and is the input's, so the same list always produces the same ids —
    /// an entity whose id moved between runs would look like a different entity to a receiver.</remarks>
    public static IReadOnlyList<string> Resolve(IEnumerable<string?> raw)
    {
        var allocator = new MqttEntityIdAllocator();
        return [.. raw.Select(allocator.Allocate)];
    }
}

/// <summary>Hands out entity ids one at a time, remembering what it has given away. For a caller
/// building an entity set incrementally; <see cref="MqttEntityId.Resolve"/> is the whole-list form.</summary>
public sealed class MqttEntityIdAllocator
{
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    /// <summary>The normalised id, made unique against everything already handed out.</summary>
    public string Allocate(string? raw)
    {
        string id = MqttEntityId.Normalise(raw);
        if (_taken.Add(id)) return id;

        // The stem is trimmed to leave room for the suffix, so a long name cannot push an id past
        // the cap and collide again after the truncation.
        for (int n = 2; ; n++)
        {
            string suffix = $"_{n}";
            string stem = id.Length + suffix.Length <= MqttEntityId.MaxLength
                ? id
                : id[..(MqttEntityId.MaxLength - suffix.Length)];
            string candidate = stem + suffix;
            if (_taken.Add(candidate)) return candidate;
        }
    }

    /// <summary>Whether an id has already been handed out.</summary>
    public bool Contains(string id) => _taken.Contains(id);
}
