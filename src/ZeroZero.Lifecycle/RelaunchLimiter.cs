using System.Globalization;
using ZeroZero.Primitives;

namespace ZeroZero.Lifecycle;

/// <summary>At most three relaunches in ten minutes, counted through a file of timestamps in the
/// product's data folder. The count is on disk because the process keeping it is the one that keeps
/// dying; anything in memory dies with it.</summary>
public sealed class RelaunchLimiter
{
    public const int Limit = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    public const string FileName = "relaunches.txt";

    private readonly ILogSink _log;
    private readonly TimeProvider _clock;
    private readonly int _limit;
    private readonly TimeSpan _window;

    public RelaunchLimiter(string dataDirectory, ILogSink? log = null)
        : this(dataDirectory, log, TimeProvider.System, Limit, Window)
    {
    }

    internal RelaunchLimiter(string dataDirectory, ILogSink? log, TimeProvider clock, int limit, TimeSpan window)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        FilePath = Path.Combine(dataDirectory, FileName);
        _log = log ?? NullLogSink.Instance;
        _clock = clock;
        _limit = limit;
        _window = window;
    }

    /// <summary>The file of timestamps.</summary>
    public string FilePath { get; }

    /// <summary>Whether one more relaunch is within the budget, recording it when it is. A file that
    /// cannot be read or written answers yes: a tray that never comes back costs more than one that
    /// comes back once too often, and the failure is logged either way.</summary>
    public bool TryRecordRelaunch()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        try
        {
            List<DateTimeOffset> recent = Read().Where(stamp => now - stamp < _window).ToList();
            if (recent.Count >= _limit)
            {
                _log.Info($"Relaunch refused: {recent.Count} relaunches in the last {_window.TotalMinutes:0} minutes (limit {_limit}).");
                return false;
            }

            recent.Add(now);
            Write(recent);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(nameof(RelaunchLimiter), ex);
            return true;
        }
    }

    private IEnumerable<DateTimeOffset> Read()
    {
        if (!File.Exists(FilePath)) yield break;

        // A line that does not parse is dropped, not fatal: a hand-edited or truncated file must
        // neither block the relaunch nor break the exit path.
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
