using System.Runtime.CompilerServices;
using Xunit;
using ZeroZero.Diagnostics;

namespace ZeroZero.Diagnostics.Tests;

/// <summary>The arms, provoked. An unhandled exception cannot be raised inside the test host without
/// ending it, so the tests that need one launch this assembly's own executable, let it die, and read
/// what the handlers wrote; the unobserved-task arm fires on the finalizer thread and is provoked
/// in-process as well.</summary>
public class CrashHandlersTests : IDisposable
{
    // The code a .NET process exits with when a managed exception ends it.
    private const int UnhandledExceptionExitCode = unchecked((int)0xE0434352);

    private static readonly string Victim = ChildProcess.OwnExecutable("ZeroZero.Diagnostics.Tests");

    private readonly Scratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    [Fact]
    public void AnUnhandledExceptionInARealProcessReachesTheSinkAndTheCrashLine()
    {
        string sink = _scratch.File("sink.log");
        string crashLine = _scratch.File("crash.log");

        var result = ChildProcess.Run(Victim, "crash-unhandled", sink, crashLine);

        Assert.Equal(UnhandledExceptionExitCode, result.ExitCode);
        Assert.Contains(Program.UnhandledMessage, result.StandardError);

        string sinkText = File.ReadAllText(sink);
        Assert.Contains($"error {CrashHandlers.UnhandledSource} System.InvalidOperationException: {Program.UnhandledMessage}", sinkText);

        string[] crashLines = File.ReadAllLines(crashLine);
        Assert.Contains(crashLines, line => line.EndsWith($"  {CrashHandlers.UnhandledSource}  System.InvalidOperationException: {Program.UnhandledMessage}"));
        Assert.Contains(crashLines, line => line.Contains(" at ") && line.Contains("Program.CrashUnhandled"));
    }

    [Fact]
    public void TheCrashLineIsWrittenWhenTheSinkItselfThrowsInARealCrash()
    {
        string crashLine = _scratch.File("crash.log");

        var result = ChildProcess.Run(Victim, "crash-unhandled", _scratch.File("unused.log"), crashLine, "sink-throws");

        Assert.Equal(UnhandledExceptionExitCode, result.ExitCode);
        Assert.Contains(Program.UnhandledMessage, File.ReadAllText(crashLine));
    }

    [Fact]
    public void ADisposedRegistrationSeesNothingOfARealCrash()
    {
        string sink = _scratch.File("sink.log");
        string crashLine = _scratch.File("crash.log");

        var result = ChildProcess.Run(Victim, "crash-unhandled", sink, crashLine, "after-dispose");

        Assert.Equal(UnhandledExceptionExitCode, result.ExitCode);
        Assert.Contains(Program.UnhandledMessage, result.StandardError);
        Assert.False(File.Exists(sink), "the sink was written after the registration was disposed");
        Assert.False(File.Exists(crashLine), "the crash line was written after the registration was disposed");
    }

    [Fact]
    public void AnUnobservedTaskExceptionInARealProcessIsReportedAndTheProcessGoesOn()
    {
        string crashLine = _scratch.File("crash.log");

        var result = ChildProcess.Run(Victim, "drop-task", crashLine);

        Assert.Equal(0, result.ExitCode);
        string text = File.ReadAllText(crashLine);
        Assert.Contains($"  {CrashHandlers.UnobservedTaskSource}  System.InvalidOperationException: {Program.UnobservedMessage}", text);
        Assert.DoesNotContain(CrashHandlers.UnhandledSource, text);
    }

    [Fact]
    public void ReportFromTheHostsOwnArmReachesTheCrashLineAndThenTheSink()
    {
        var sink = new RecordingSink();
        var appender = new CrashLineAppender(_scratch.File("crash.log"));
        using var handlers = CrashHandlers.Register(new CrashHandlerOptions { Sink = sink, CrashLine = appender });
        var exception = new InvalidOperationException("from the window");

        handlers.Report("Application.UnhandledException", exception);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(("error", "Application.UnhandledException", exception), entry);
        Assert.Contains("  Application.UnhandledException  System.InvalidOperationException: from the window", File.ReadAllText(appender.FilePath));
    }

    [Fact]
    public void ReportOutlivesASinkThatThrows()
    {
        var appender = new CrashLineAppender(_scratch.File("crash.log"));
        using var handlers = CrashHandlers.Register(new CrashHandlerOptions { Sink = new ThrowingSink(), CrashLine = appender });

        var exception = Record.Exception(() => handlers.Report("source", new InvalidOperationException("still written")));

        Assert.Null(exception);
        Assert.Contains("still written", File.ReadAllText(appender.FilePath));
    }

    [Fact]
    public void ReportWithoutACrashLineStillReachesTheSink()
    {
        var sink = new RecordingSink();
        using var handlers = CrashHandlers.Register(new CrashHandlerOptions { Sink = sink, CrashLine = null });

        handlers.Report("source", null);

        Assert.Equal(("error", "source", null), Assert.Single(sink.Entries));
    }

    [Fact]
    public void AnUnobservedTaskReachesALiveRegistrationAndNotADisposedOne()
    {
        var disposedSink = new RecordingSink();
        var liveSink = new RecordingSink();
        var disposed = CrashHandlers.Register(new CrashHandlerOptions { Sink = disposedSink });
        disposed.Dispose();
        using var live = CrashHandlers.Register(new CrashHandlerOptions { Sink = liveSink });
        string marker = "unobserved in the test host " + Guid.NewGuid().ToString("N");

        FaultAndDrop(marker);
        bool seen = false;
        for (int i = 0; i < 20 && !seen; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            seen = liveSink.Entries.Any(entry => entry.Exception?.Message == marker);
        }

        Assert.True(seen, "the live registration never saw the unobserved exception");
        var entry = liveSink.Entries.Single(entry => entry.Exception?.Message == marker);
        Assert.Equal(CrashHandlers.UnobservedTaskSource, entry.Text);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.DoesNotContain(disposedSink.Entries, entry => entry.Exception?.Message == marker);
    }

    [Fact]
    public void AReportedUnobservedTaskExceptionIsMarkedObservedForWhoeverListensNext()
    {
        var sink = new RecordingSink();
        using var handlers = CrashHandlers.Register(new CrashHandlerOptions { Sink = sink });
        string marker = "observed after report " + Guid.NewGuid().ToString("N");
        bool? observedWhenTheNextSubscriberRan = null;

        // Subscribed after the registration, so it runs after the handler and sees what it left.
        EventHandler<UnobservedTaskExceptionEventArgs> probe = (_, e) =>
        {
            if (e.Exception.InnerExceptions.Any(inner => inner.Message == marker))
                observedWhenTheNextSubscriberRan = e.Observed;
        };
        TaskScheduler.UnobservedTaskException += probe;
        try
        {
            FaultAndDrop(marker);
            for (int i = 0; i < 20 && observedWhenTheNextSubscriberRan is null; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= probe;
        }

        Assert.True(observedWhenTheNextSubscriberRan, "the probe never saw the exception, or saw it unobserved");
        Assert.Contains(sink.Entries, entry => entry.Exception?.Message == marker);
    }

    [Fact]
    public void DisposingTwiceUnwiresOnce()
    {
        var handlers = CrashHandlers.Register(new CrashHandlerOptions { Sink = new RecordingSink() });

        handlers.Dispose();
        var exception = Record.Exception(handlers.Dispose);

        Assert.Null(exception);
    }

    [Fact]
    public void RegisterRefusesNoOptionsAndNoSink()
    {
        Assert.Throws<ArgumentNullException>(() => CrashHandlers.Register(null!));
        Assert.Throws<ArgumentNullException>(() => CrashHandlers.Register(new CrashHandlerOptions { Sink = null! }));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FaultAndDrop(string message)
    {
        Task faulted = Task.Run(() => throw new InvalidOperationException(message));
        while (!faulted.IsCompleted) Thread.Sleep(10);
    }
}
