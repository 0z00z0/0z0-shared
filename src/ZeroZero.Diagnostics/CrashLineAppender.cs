using System.Globalization;
using System.Text;
using ZeroZero.Primitives;

namespace ZeroZero.Diagnostics;

/// <summary>The last place a crash is written: one entry appended to a plain text file, and never an
/// exception back to the caller.</summary>
/// <remarks>It runs inside the handlers a process falls into when everything else has failed, where
/// a second exception hides the first. Every failure to write — a locked file, a path that turns out
/// to be a file, a drive that is gone — answers false and loses the entry rather than the crash.
/// Construction validates the path and may throw; appending never does. The stamp is local time
/// with its offset, so an entry can be set beside a dump file's timestamp without conversion.</remarks>
public sealed class CrashLineAppender : ILogSink
{
    private const string StampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

    public CrashLineAppender(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    /// <summary>Where the entries go, as a full path.</summary>
    public string FilePath { get; }

    public void Info(string message) => Append(message);

    public void Error(string source, Exception? ex) => Append(source, ex);

    /// <summary>Appends one stamped line. False when the entry could not be written.</summary>
    public bool Append(string message) => Write(Stamp() + "  " + (message ?? ""));

    /// <summary>Appends one stamped entry: the source, the exception's type and message on the first
    /// line, then the whole exception — stack and inner exceptions — beneath it, because the dump
    /// may never be read and the entry is then all there is. False when it could not be written.</summary>
    public bool Append(string source, Exception? ex)
    {
        var entry = new StringBuilder();
        entry.Append(Stamp()).Append("  ").Append(source ?? "");
        if (ex is null)
        {
            entry.Append("  (no exception)");
        }
        else
        {
            entry.Append("  ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
            entry.AppendLine().Append(ex);
        }
        return Write(entry.ToString());
    }

    private bool Write(string entry)
    {
        try
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // ReadWrite sharing: the host's own log reader, or a second instance of the process,
            // must not turn the crash line into a sharing violation.
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(Encoding.UTF8.GetBytes(entry + Environment.NewLine));
            // The process is about to die; the entry must reach the disk, not a cache a power cut empties.
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Stamp() => DateTimeOffset.Now.ToString(StampFormat, CultureInfo.InvariantCulture);
}
