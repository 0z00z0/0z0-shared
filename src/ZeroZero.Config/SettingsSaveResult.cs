namespace ZeroZero.Config;

/// <summary>The outcome of a save. A save never throws, so this is how a caller learns that a
/// locked or full disk refused the write — a surface reporting "saved" against a failure is a
/// defect, not a nicety.</summary>
public readonly record struct SettingsSaveResult
{
    private SettingsSaveResult(bool saved, Exception? error)
    {
        Saved = saved;
        Error = error;
    }

    /// <summary>True when the stored state is on disk, including when it was already there and
    /// nothing needed writing.</summary>
    public bool Saved { get; }

    /// <summary>Why the write failed, or null when it did not.</summary>
    public Exception? Error { get; }

    /// <summary>The state reached the file.</summary>
    public static SettingsSaveResult Success { get; } = new(true, null);

    /// <summary>The state did not reach the file, and has been rolled back in memory.</summary>
    public static SettingsSaveResult Failed(Exception error) => new(false, error);
}
