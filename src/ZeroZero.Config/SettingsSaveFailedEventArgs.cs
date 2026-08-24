namespace ZeroZero.Config;

/// <summary>A save that did not reach the file. Carried by an event as well as a return value, so
/// a caller behind a void-returning abstraction can still report the failure.</summary>
public sealed class SettingsSaveFailedEventArgs(string filePath, Exception error) : EventArgs
{
    /// <summary>The file the write was aimed at.</summary>
    public string FilePath { get; } = filePath;

    /// <summary>Why the write failed.</summary>
    public Exception Error { get; } = error;
}
