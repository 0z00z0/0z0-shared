using System.Globalization;
using ZeroZero.Primitives;

namespace ZeroZero.Startup;

/// <summary>
/// The record that the person exited on purpose. A probe that finds it leaves the application down;
/// without it the watchdog cannot tell a deliberate exit from a crash, and would start the
/// application again against the person's own choice.
/// </summary>
/// <remarks>
/// A file rather than anything in memory, because the two sides never share a process: the exit is
/// written by the application on its way out, and read by the copy the scheduler starts afterwards.
/// <para>The file carries the moment it was written, and a record older than <see cref="Lifetime"/>
/// counts as absent: a marker left behind by a clear that failed would otherwise keep the
/// application down for the life of the installation.</para>
/// </remarks>
public sealed class WatchdogHold
{
    /// <summary>How long a record of a deliberate exit means anything. Past this the person who made
    /// that choice is no longer making it, and a stale marker is worse than none.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private readonly string _path;
    private readonly ILogSink _log;
    private readonly TimeProvider _clock;

    private bool _expirySaid;
    private bool _unreadableSaid;

    public WatchdogHold(string path, ILogSink? log = null)
        : this(path, log, TimeProvider.System)
    {
    }

    internal WatchdogHold(string path, ILogSink? log, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _log = log ?? NullLogSink.Instance;
        _clock = clock;
    }

    /// <summary>Whether a deliberate exit is on record and still current. A marker that is there but
    /// cannot be read counts as held: starting the application against the person's choice costs
    /// more than leaving it down, and the failure is logged either way.</summary>
    public bool IsHeld
    {
        get
        {
            try
            {
                if (!File.Exists(_path)) return false;

                string written = File.ReadAllText(_path).Trim();
                if (!DateTimeOffset.TryParseExact(written, "O", CultureInfo.InvariantCulture,
                                                  DateTimeStyles.RoundtripKind, out DateTimeOffset stamp))
                {
                    Say(ref _unreadableSaid, "The watchdog hold marker carries no readable timestamp, so the application stays down.");
                    return true;
                }

                if (_clock.GetUtcNow() - stamp <= Lifetime) return true;

                Say(ref _expirySaid, $"The watchdog hold marker is older than {Lifetime.TotalDays:0} days, so it no longer holds the application down.");
                return false;
            }
            catch (Exception ex)
            {
                _log.Error(nameof(WatchdogHold), ex);
                return true;
            }
        }
    }

    /// <summary>Records a deliberate exit, and says whether it landed. Written on the way out of the
    /// application, so a failure here must not stop the exit — but the caller is told, because the
    /// next probe starts the application again against the person's choice.</summary>
    public bool Hold()
    {
        try
        {
            string? folder = Path.GetDirectoryName(_path);
            if (folder is { Length: > 0 }) Directory.CreateDirectory(folder);
            File.WriteAllText(_path, _clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(WatchdogHold), ex);
            return false;
        }
    }

    /// <summary>Clears the record, on every start the person asked for, so the backstop is armed
    /// again. True where nothing is held afterwards, including where there was nothing to clear;
    /// false where the marker is still there, which leaves the application without its backstop
    /// until a start that can remove it.</summary>
    public bool Release()
    {
        try
        {
            File.Delete(_path);
            return true;
        }
        // Nothing to clear is the ordinary case on a first run. File.Delete is silent about a
        // missing file but not about the folder it would have been in.
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(WatchdogHold), ex);
            return false;
        }
    }

    // Once per instance: a start may read the marker more than once, and a repeated line says
    // nothing the first did not.
    private void Say(ref bool said, string message)
    {
        if (said) return;
        said = true;
        _log.Info(message);
    }
}
