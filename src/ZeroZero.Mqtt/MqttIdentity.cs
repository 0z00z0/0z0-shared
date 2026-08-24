using System.Text;

namespace ZeroZero.Mqtt;

/// <summary>The device id: sanitisation, the machine-name default, validation of a user-typed value,
/// and which of the two is actually in force. Pure — no client, no settings, no clock.</summary>
/// <remarks>The id is the identity end to end. It is the MQTT client id, the <c>unique_id</c> stem,
/// the device identifier and every topic segment below the root, so a change to it orphans every
/// retained topic the old id owned.</remarks>
public static class MqttIdentity
{
    /// <summary>Longest device id accepted from the user — keeps the id readable inside a topic path.</summary>
    public const int MaxLength = 48;

    /// <summary>Lower-cases and reduces to [a-z0-9_] (topic-safe), reporting whether anything
    /// alphanumeric survived.</summary>
    private static (string Text, bool HasAlnum) Sanitise(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        bool hasAlnum = false;
        foreach (char c in raw.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) { sb.Append(c); hasAlnum = true; }
            else sb.Append('_');
        }
        return (sb.ToString(), hasAlnum);
    }

    /// <summary>Stable per-machine device id, <c>&lt;topicRoot&gt;_&lt;sanitised machine name&gt;</c>.
    /// A machine name of only punctuation would sanitise to all underscores, so it falls back to
    /// "device".</summary>
    public static string Default(string topicRoot, string machineName)
    {
        var (text, hasAlnum) = Sanitise(machineName);
        return $"{topicRoot}_{(hasAlnum ? text : "device")}";
    }

    /// <summary>A user-typed device id reduced to the same alphabet <see cref="Default"/> produces,
    /// capped at <see cref="MaxLength"/>. The topic-root prefix is deliberately not forced — only the
    /// machine-name default carries it.</summary>
    /// <remarks>Each rejected character becomes one underscore rather than collapsing a run, because
    /// this id is what every retained topic already carries: collapsing would rename the device on
    /// every installation whose id was derived under the old rule. <see cref="MqttEntityId"/> is
    /// under no such constraint and does collapse.</remarks>
    public static string Normalise(string raw)
    {
        var (text, _) = Sanitise(raw.Trim());
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }

    /// <summary>Why a user-typed device id is unusable, or null when it is fine. Blank is not an
    /// error — an empty setting means "use the machine-name default".</summary>
    /// <remarks>Uniqueness is a constraint this cannot check and does not claim to. The id has to be
    /// unique across every installation publishing to the one broker, because it is the MQTT client id
    /// — two machines sharing it disconnect each other in a loop — and the <c>unique_id</c> stem, so
    /// they would also overwrite each other's entities. Nothing local can see the other machines, so a
    /// host offering this field says so where the user types it. The machine-name default is unique by
    /// construction.</remarks>
    public static string? Validate(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxLength) return $"An id can be at most {MaxLength} characters.";
        if (!Sanitise(trimmed).HasAlnum) return "An id must contain at least one letter or digit.";
        return null;
    }

    /// <summary>The device id actually published under: the sanitised <paramref name="custom"/>, or
    /// the machine-name default. A custom value that sanitises to nothing usable — reachable by
    /// hand-editing the settings file past the validator — falls back too.</summary>
    public static string Effective(string? custom, string topicRoot, string machineName)
    {
        if (string.IsNullOrWhiteSpace(custom)) return Default(topicRoot, machineName);
        string id = Normalise(custom);
        return id.Any(char.IsAsciiLetterOrDigit) ? id : Default(topicRoot, machineName);
    }
}
