namespace ZeroZero.Config.Watch;

/// <summary>Something the watcher does on its own thread did not work.</summary>
/// <param name="path">The file being watched.</param>
/// <param name="error">What stopped it.</param>
public sealed class SettingsWatchFailedEventArgs(string path, Exception error) : EventArgs
{
    /// <summary>The file being watched.</summary>
    public string Path { get; } = path;

    /// <summary>What stopped it. A re-read that failed, a subscriber that threw, or the operating
    /// system dropping notifications faster than they could be taken — or a
    /// <see cref="SettingsWatchObstructedException"/>, which is not a failure at all but the store
    /// saying that nothing will reload until the file is repaired.</summary>
    public Exception Error { get; } = error;
}
