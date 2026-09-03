namespace ZeroZero.Config.Watch;

/// <summary>The store cannot see the part of the file the watcher is watching, so a re-read moves
/// nothing however the file is edited.</summary>
/// <remarks>Not a failure of the watcher and not an error the file raised: the store read the file
/// successfully and reported that something stands between it and the values. A section addressed
/// under one spelling while the document carries another is the case this exists for — it reads as
/// its type's defaults for ever and every write to it is refused, so nothing a person types in the
/// file ever arrives. Reported through <see cref="SettingsWatcher{T}.Failed"/> because a change
/// event would be a lie and silence would leave the person with nothing to go on.</remarks>
public sealed class SettingsWatchObstructedException : InvalidOperationException
{
    internal SettingsWatchObstructedException(string path, string reason)
        : base($"Nothing in '{path}' will reload while this stands: {reason}")
    {
        Path = path;
        Reason = reason;
    }

    /// <summary>The file being watched.</summary>
    public string Path { get; }

    /// <summary>What the store said is in the way, in its own words.</summary>
    public string Reason { get; }
}
