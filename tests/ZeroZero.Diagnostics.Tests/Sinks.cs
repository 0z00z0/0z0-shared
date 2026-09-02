using ZeroZero.Primitives;

namespace ZeroZero.Diagnostics.Tests;

/// <summary>Remembers every call, for a test in the same process.</summary>
public sealed class RecordingSink : ILogSink
{
    private readonly List<(string Kind, string Text, Exception? Exception)> _entries = [];

    public IReadOnlyList<(string Kind, string Text, Exception? Exception)> Entries
    {
        get { lock (_entries) return _entries.ToArray(); }
    }

    public void Info(string message)
    {
        lock (_entries) _entries.Add(("info", message, null));
    }

    public void Error(string source, Exception? ex)
    {
        lock (_entries) _entries.Add(("error", source, ex));
    }
}

/// <summary>Writes every call to a file, for a test that reads what a child process logged.</summary>
public sealed class FileSink(string path) : ILogSink
{
    public void Info(string message) => File.AppendAllText(path, "info " + message + Environment.NewLine);

    public void Error(string source, Exception? ex) =>
        File.AppendAllText(path, $"error {source} {ex?.GetType().FullName}: {ex?.Message}{Environment.NewLine}");
}

/// <summary>The host's logging is what failed.</summary>
public sealed class ThrowingSink : ILogSink
{
    public void Info(string message) => throw new IOException("the log is gone");

    public void Error(string source, Exception? ex) => throw new IOException("the log is gone");
}
