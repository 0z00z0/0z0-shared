namespace ZeroZero.Config;

/// <summary>Where a settings file that cannot be parsed is preserved, and how many copies survive.</summary>
/// <param name="Keep">How many quarantined copies to retain. Zero replaces an unreadable file
/// without preserving it; older copies beyond the count are deleted, newest first.</param>
/// <param name="Directory">Where copies are written. Null keeps them beside the settings file.</param>
public sealed record SettingsFileQuarantine(int Keep = 3, string? Directory = null)
{
    /// <summary>Three copies, kept beside the settings file.</summary>
    public static SettingsFileQuarantine Default { get; } = new();

    /// <summary>Preserve nothing.</summary>
    public static SettingsFileQuarantine Off { get; } = new(0);
}
