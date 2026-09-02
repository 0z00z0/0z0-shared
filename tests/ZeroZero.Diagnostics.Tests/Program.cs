using System.Runtime.CompilerServices;
using ZeroZero.Diagnostics;
using ZeroZero.Primitives;

namespace ZeroZero.Diagnostics.Tests;

/// <summary>The crash victim. The tests launch this assembly's own executable with one of the modes
/// below, so the exception is genuinely unhandled in a genuinely separate process, and read what the
/// handlers wrote from the files the arguments name. The test host never enters here.</summary>
public static class Program
{
    public const string UnhandledMessage = "provoked unhandled exception 7f3c1a";
    public const string UnobservedMessage = "provoked unobserved task exception 9b2e4d";

    public static int Main(string[] args)
    {
        switch (args.Length > 0 ? args[0] : "")
        {
            case "crash-unhandled":
                return CrashUnhandled(sinkFile: args[1], crashLineFile: args[2], variant: args.Length > 3 ? args[3] : "");
            case "drop-task":
                return DropTask(crashLineFile: args[1]);
            default:
                Console.Error.WriteLine("crash victim: crash-unhandled <sink> <crashline> [sink-throws|after-dispose] | drop-task <crashline>");
                return 2;
        }
    }

    private static int CrashUnhandled(string sinkFile, string crashLineFile, string variant)
    {
        ILogSink sink = variant == "sink-throws" ? new ThrowingSink() : new FileSink(sinkFile);
        var handlers = CrashHandlers.Register(new CrashHandlerOptions
        {
            Sink = sink,
            CrashLine = new CrashLineAppender(crashLineFile),
        });
        if (variant == "after-dispose") handlers.Dispose();

        throw new InvalidOperationException(UnhandledMessage);
    }

    private static int DropTask(string crashLineFile)
    {
        CrashHandlers.Register(new CrashHandlerOptions
        {
            Sink = new CrashLineAppender(crashLineFile),
            CrashLine = null,
        });

        FaultAndDrop();
        for (int i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return 0;
    }

    /// <summary>A faulted task nothing holds on to, so the finalizer raises it as unobserved. Not
    /// inlined, so the reference does not outlive the call in the caller's frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FaultAndDrop()
    {
        Task faulted = Task.Run(() => throw new InvalidOperationException(UnobservedMessage));
        while (!faulted.IsCompleted) Thread.Sleep(10);
    }
}
