using ZeroZero.Primitives;

namespace ZeroZero.Diagnostics.Dumps;

/// <summary>Bounds the dumps kept on disk. Windows Error Reporting bounds them too, but only for the
/// registration it currently holds: a lowered count, a disarmed executable and an older build's name
/// all leave files it will never touch again.</summary>
public static class DumpRetention
{
    /// <summary>Deletes the oldest dumps of <paramref name="executableName"/> beyond
    /// <paramref name="keep"/>, newest by last write kept. Returns how many were deleted; a
    /// directory that does not exist yields zero, and a file that will not delete is logged and left.</summary>
    public static int Prune(string directory, string executableName, int keep, ILogSink log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        ArgumentOutOfRangeException.ThrowIfNegative(keep);
        ArgumentNullException.ThrowIfNull(log);

        if (!Directory.Exists(directory)) return 0;

        // Windows Error Reporting names a dump <image>.<pid>.dmp.
        var dumps = new DirectoryInfo(directory)
            .EnumerateFiles(executableName + ".*.dmp", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(keep);

        int deleted = 0;
        foreach (FileInfo dump in dumps)
        {
            try
            {
                dump.Delete();
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Error($"Crash dump retention could not delete {dump.Name}", ex);
            }
        }

        if (deleted > 0) log.Info($"Removed {deleted} old crash dump(s) of {executableName}, keeping {keep}.");
        return deleted;
    }
}
