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
/// </remarks>
public sealed class WatchdogHold
{
    private readonly string _path;
    private readonly ILogSink _log;

    public WatchdogHold(string path, ILogSink? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _log = log ?? NullLogSink.Instance;
    }

    /// <summary>Whether a deliberate exit is on record. False where the file cannot be read at all,
    /// so an unreadable marker costs a relaunch rather than leaving the application down for good.</summary>
    public bool IsHeld
    {
        get
        {
            try { return File.Exists(_path); }
            catch (Exception ex) { _log.Error(nameof(WatchdogHold), ex); return false; }
        }
    }

    /// <summary>Records a deliberate exit. Written on the way out of the application, so a failure
    /// here costs the record and must not stop the exit.</summary>
    public void Hold()
    {
        try
        {
            string? folder = Path.GetDirectoryName(_path);
            if (folder is { Length: > 0 }) Directory.CreateDirectory(folder);
            File.WriteAllText(_path, DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { _log.Error(nameof(WatchdogHold), ex); }
    }

    /// <summary>Clears the record, on every start the person asked for, so the backstop is armed
    /// again. Best-effort: a marker that cannot be deleted costs nothing until the next exit.</summary>
    public void Release()
    {
        try { File.Delete(_path); }
        // Nothing to clear is the ordinary case on a first run. File.Delete is silent about a
        // missing file but not about the folder it would have been in.
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) { _log.Error(nameof(WatchdogHold), ex); }
    }
}
