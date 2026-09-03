using System.Text.Json;

namespace ZeroZero.Config.Sections;

/// <summary>Everything a <see cref="SectionedSettingsFile"/> needs to find and shape its document.
/// The host owns the directory; the document's own name and shape are the application's.</summary>
/// <param name="Directory">The folder holding the file. Created on first save if absent.</param>
/// <param name="FileName">The file name, with no directory separator in it.</param>
public sealed record SectionedSettingsOptions(string Directory, string FileName)
{
    /// <summary>How a section is read and written. Defaults to the config assembly's forgiving
    /// settings: indented, any casing accepted on read, comments and trailing commas tolerated, and
    /// every enum written as its declared member name.</summary>
    public JsonSerializerOptions Serialiser { get; init; } = SettingsFileOptions.DefaultSerialiser;

    /// <summary>What happens to a document this build cannot read.</summary>
    public SettingsFileQuarantine Quarantine { get; init; } = SettingsFileQuarantine.Default;

    /// <summary>Where change and failure notifications are raised. Null raises them on the thread
    /// that made the change; a consumer with a user interface passes the context captured on its own
    /// thread.</summary>
    public SynchronizationContext? NotificationContext { get; init; }

    /// <summary>The document shape this build writes, stamped as the <c>version</c> key when the
    /// document carries none. A document declaring a higher version is neither read nor written.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The order sections take, consulted only when adding a section the document does not
    /// carry. A section already in the file keeps the position the file gives it, and a name absent
    /// from this list is appended last.</summary>
    public IReadOnlyList<string> SectionOrder { get; init; } = [];
}
