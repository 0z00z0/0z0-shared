using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroZero.Config;

/// <summary>Everything a <see cref="SettingsFile{T}"/> needs to find and shape its file. The host
/// owns the directory; the module using the file owns the name.</summary>
/// <param name="Directory">The folder holding the file. Created on first save if absent.</param>
/// <param name="FileName">The file name, with no directory separator in it.</param>
public sealed record SettingsFileOptions(string Directory, string FileName)
{
    /// <summary>How the file is read and written. Defaults to <see cref="DefaultSerialiser"/>.</summary>
    public JsonSerializerOptions Serialiser { get; init; } = DefaultSerialiser;

    /// <summary>What happens to a file that cannot be parsed.</summary>
    public SettingsFileQuarantine Quarantine { get; init; } = SettingsFileQuarantine.Default;

    /// <summary>Where change and failure notifications are raised. Null raises them on the thread
    /// that made the change; a UI consumer passes the context captured on its own thread.</summary>
    public SynchronizationContext? NotificationContext { get; init; }

    /// <summary>Indented, forgiving of hand edits, and writing every enum as its declared member
    /// name — a name survives a member being renumbered or reordered, a number does not.</summary>
    public static JsonSerializerOptions DefaultSerialiser { get; } = CreateDefaultSerialiser();

    /// <summary>A fresh copy of the default settings, for a caller that needs to add a converter.</summary>
    public static JsonSerializerOptions CreateSerialiser()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // No naming policy: the member name as declared is what a later file must still match.
        // Reading accepts any casing, and a number left by an older writer.
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static JsonSerializerOptions CreateDefaultSerialiser()
    {
        var options = CreateSerialiser();
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
