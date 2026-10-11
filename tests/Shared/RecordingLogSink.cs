using ZeroZero.Primitives;

namespace ZeroZero.Tests;

/// <summary>Keeps every line and error it is handed, for a test to read back. Safe from any thread.</summary>
internal sealed class RecordingLogSink : ILogSink
{
    private readonly Lock _lock = new();

    public List<string> Infos { get; } = [];
    public List<(string Source, Exception? Error)> Errors { get; } = [];

    public void Info(string message)
    {
        lock (_lock) Infos.Add(message);
    }

    public void Error(string source, Exception? ex)
    {
        lock (_lock) Errors.Add((source, ex));
    }
}
