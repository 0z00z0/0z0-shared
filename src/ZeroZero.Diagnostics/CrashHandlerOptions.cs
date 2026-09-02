using ZeroZero.Primitives;

namespace ZeroZero.Diagnostics;

/// <summary>What the crash handlers are wired to.</summary>
public sealed record CrashHandlerOptions
{
    /// <summary>The host's log. A sink that throws while reporting a crash is caught and ignored, so
    /// the crash it was reporting is not hidden behind the sink's own failure.</summary>
    public required ILogSink Sink { get; init; }

    /// <summary>The never-throws file the crash reaches before the sink, and reaches even when the
    /// sink is what failed. Null when the host writes no such file.</summary>
    public CrashLineAppender? CrashLine { get; init; }
}
