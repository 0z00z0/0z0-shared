using ZeroZero.Primitives;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>A host's logger, in the one respect that matters here: it writes to one file until the
/// application closes it on the way out of <c>Main</c>, and everything written after that goes to a
/// second file instead. Which of the two files an entry landed in is what says whether it was
/// written before the close or after it.</summary>
internal sealed class ClosingLogSink(string openPath, string closedPath) : ILogSink
{
    private readonly FileLogSink _open = new(openPath);
    private readonly FileLogSink _afterClose = new(closedPath);

    private volatile bool _closed;

    /// <summary>The application letting its logger go, as the last thing it does.</summary>
    public void Close()
    {
        _open.Info("CLOSED");
        _closed = true;
    }

    public void Info(string message) => Current.Info(message);

    public void Error(string source, Exception? ex) => Current.Error(source, ex);

    private ILogSink Current => _closed ? _afterClose : _open;
}
