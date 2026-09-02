namespace ZeroZero.Diagnostics;

/// <summary>Where an exception nothing caught is reported. The two process-wide arms are wired here;
/// the host's own arm — a UI framework's unhandled-exception event, whose Handled decision is the
/// host's alone — calls <see cref="Report"/> so all three land in the one place.</summary>
/// <remarks>Register it first in startup, before anything that can throw. Disposing unwires both
/// arms, which a test host needs and an application never does.</remarks>
public sealed class CrashHandlers : IDisposable
{
    public const string UnhandledSource = "AppDomain.UnhandledException";
    public const string UnobservedTaskSource = "TaskScheduler.UnobservedTaskException";

    private readonly CrashHandlerOptions _options;
    private int _disposed;

    private CrashHandlers(CrashHandlerOptions options) => _options = options;

    public static CrashHandlers Register(CrashHandlerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Sink);

        var handlers = new CrashHandlers(options);
        AppDomain.CurrentDomain.UnhandledException += handlers.OnUnhandled;
        TaskScheduler.UnobservedTaskException += handlers.OnUnobservedTask;
        return handlers;
    }

    /// <summary>Reports a crash from any arm: the crash line first, because it never throws, then
    /// the host's sink, guarded, because a sink that fails here would hide the crash.</summary>
    public void Report(string source, Exception? ex)
    {
        _options.CrashLine?.Append(source, ex);
        try
        {
            _options.Sink.Error(source, ex);
        }
        catch
        {
            // The crash line already holds the entry; a failure of the sink itself has nowhere left to go.
        }
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs e) =>
        Report(UnhandledSource, e.ExceptionObject as Exception);

    private void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // One faulted task is one aggregate around one exception; the inner one is what a reader wants.
        Exception reported = e.Exception.InnerExceptions.Count == 1 ? e.Exception.InnerExceptions[0] : e.Exception;
        Report(UnobservedTaskSource, reported);
        // Reported is observed. The runtime no longer ends a process over an unobserved task
        // exception whatever its configuration says (measured on .NET 10), so this only tells a
        // later subscriber that the exception has been dealt with.
        e.SetObserved();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTask;
    }
}
