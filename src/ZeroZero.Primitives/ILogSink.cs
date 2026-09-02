namespace ZeroZero.Primitives;

/// <summary>Where a component says what it did. The host owns the logging framework; this is the
/// whole of what a component needs from it.</summary>
public interface ILogSink
{
    void Info(string message);

    /// <summary>An exception is passed whole so the host decides how much of it to record. A component
    /// that handles a credential sanitises first — type and message only — before it reaches here.</summary>
    void Error(string source, Exception? ex);
}

/// <summary>The default: nothing is recorded, so a consumer that has not supplied a sink still runs.
/// One shared instance, so every component's default is the same object.</summary>
public sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();

    private NullLogSink() { }

    public void Info(string message) { }

    public void Error(string source, Exception? ex) { }
}
