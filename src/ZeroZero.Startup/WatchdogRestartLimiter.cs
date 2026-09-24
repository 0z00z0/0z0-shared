using System.Globalization;
using ZeroZero.Primitives;

namespace ZeroZero.Startup;

/// <summary>
/// The bound on the probe's restarts: at most <see cref="Limit"/> probe-started runs inside
/// <see cref="Window"/> where the application never stayed up <see cref="StaysUp"/>. Past that the
/// probe is starting something that cannot run, and starting it again every few minutes achieves
/// nothing a person has not already noticed.
/// </summary>
/// <remarks>
/// The count is on disk because the process keeping it is the one that keeps dying; anything in
/// memory dies with it. The file sits beside the hold marker, in the folder the application already
/// names for the watchdog's state.
/// <para>A start the person asked for clears the count: someone is at the machine, so whatever the
/// loop was, it is over.</para>
/// <para><b>How a run's length is known without the dead run reporting it.</b> A probe start is
/// recorded only where the application genuinely came up, because a probe that finds a live
/// instance exits at the instance gate. The gap between two recorded starts is therefore the
/// previous run's length plus the wait for the next probe, and that wait is at most the probe
/// interval. A gap longer than the interval plus <see cref="StaysUp"/> means the run outlasted
/// <see cref="StaysUp"/>, and the count starts again. The gap bounds the run's length from above,
/// so a run that only just passed the bar may still be counted.</para>
/// </remarks>
public sealed class WatchdogRestartLimiter
{
    /// <summary>How many probe-started runs inside the window end the probing.</summary>
    public const int Limit = 3;

    /// <summary>How long the count reaches back.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>How long a run has to last for the application to count as having come up.</summary>
    public static readonly TimeSpan StaysUp = TimeSpan.FromMinutes(2);

    public const string FileName = "watchdog-restarts.txt";

    private readonly ILogSink _log;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _probeInterval;
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly TimeSpan _staysUp;

    /// <param name="stateDirectory">Where the count is kept. The folder holding the hold marker.</param>
    /// <param name="probeInterval">How often the probe runs, which is what turns the gap between
    /// two starts into a statement about how long the run between them lasted.</param>
    public WatchdogRestartLimiter(string stateDirectory, TimeSpan probeInterval, ILogSink? log = null)
        : this(stateDirectory, probeInterval, log, TimeProvider.System, Limit, Window, StaysUp)
    {
    }

    internal WatchdogRestartLimiter(string stateDirectory, TimeSpan probeInterval, ILogSink? log,
                                    TimeProvider clock, int limit, TimeSpan window, TimeSpan staysUp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        FilePath = Path.Combine(stateDirectory, FileName);
        _log = log ?? NullLogSink.Instance;
        _clock = clock;
        _probeInterval = probeInterval;
        _limit = limit;
        _window = window;
        _staysUp = staysUp;
    }

    /// <summary>The file of timestamps.</summary>
    public string FilePath { get; }

    /// <summary>Records this start and says whether the probe should keep running. False once the
    /// bound is reached, and said once: nothing repeats the line, because the probing it describes
    /// has stopped. A file that cannot be read or written answers true — a backstop lost to a
    /// transient file error costs more than one restart too many — and the failure is logged.</summary>
    public bool RecordStart(WatchdogStartCause cause)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        try
        {
            if (cause == WatchdogStartCause.Person)
            {
                Clear();
                return true;
            }

            List<DateTimeOffset> starts = Read().Where(stamp => now - stamp < _window).Order().ToList();

            // The run before this one outlasted the bar, so it is not part of a loop and neither is
            // anything before it.
            if (starts.Count > 0 && now - starts[^1] > _probeInterval + _staysUp) starts.Clear();

            starts.Add(now);
            Write(starts);

            if (starts.Count < _limit) return true;

            _log.Info($"Watchdog probing stopped: {starts.Count} starts in the last {_window.TotalMinutes:0} minutes, "
                      + $"none of which stayed up {_staysUp.TotalMinutes:0} minutes. It resumes when a person starts the application.");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(nameof(WatchdogRestartLimiter), ex);
            return true;
        }
    }

    private void Clear()
    {
        try { File.Delete(FilePath); }
        // Nothing to clear is the ordinary case. File.Delete is silent about a missing file but not
        // about the folder it would have been in.
        catch (DirectoryNotFoundException) { }
    }

    private IEnumerable<DateTimeOffset> Read()
    {
        if (!File.Exists(FilePath)) yield break;

        // A line that does not parse is dropped rather than fatal: a hand-edited or truncated file
        // must not stop the application starting.
        foreach (string line in File.ReadAllLines(FilePath))
            if (DateTimeOffset.TryParseExact(line.Trim(), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset stamp))
                yield return stamp;
    }

    private void Write(IEnumerable<DateTimeOffset> stamps)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllLines(FilePath, stamps.Select(stamp => stamp.ToString("O", CultureInfo.InvariantCulture)));
    }
}
