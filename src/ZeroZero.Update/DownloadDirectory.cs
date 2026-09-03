using ZeroZero.Primitives;

namespace ZeroZero.Update;

/// <summary>Where a download lands: a directory created for it under the temporary folder, named
/// by the prefix and a fresh identifier, so nothing can be planted at the path ahead of time.</summary>
public static class DownloadDirectory
{
    public static string Create(string prefix, string? root = null)
    {
        ValidatePrefix(prefix);
        string path = Path.Combine(root ?? Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Removes every directory of the prefix's shape older than <paramref name="olderThan"/>,
    /// except <paramref name="except"/>. A directory that will not delete — an installer still
    /// running from it — is logged and left for the next sweep.</summary>
    /// <returns>How many were removed.</returns>
    public static int Sweep(string prefix, TimeSpan olderThan, ILogSink log, string? root = null, string? except = null)
    {
        ValidatePrefix(prefix);
        ArgumentNullException.ThrowIfNull(log);

        string parent = root ?? Path.GetTempPath();
        if (!Directory.Exists(parent)) return 0;

        DateTime cutoff = DateTime.UtcNow - olderThan;
        int removed = 0;
        foreach (string directory in Directory.EnumerateDirectories(parent, $"{prefix}-*"))
        {
            if (!IsOfShape(Path.GetFileName(directory), prefix)) continue;
            if (except is not null && string.Equals(Path.GetFullPath(directory), Path.GetFullPath(except), StringComparison.OrdinalIgnoreCase)) continue;
            if (Directory.GetLastWriteTimeUtc(directory) > cutoff) continue;

            try
            {
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Error(nameof(DownloadDirectory), ex);
            }
        }
        return removed;
    }

    internal static void ValidatePrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (!prefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-'))
            throw new ArgumentException($"'{prefix}' is not a directory prefix: letters, digits, dots and hyphens only.", nameof(prefix));
    }

    private static bool IsOfShape(string name, string prefix)
    {
        if (name.Length != prefix.Length + 1 + 32) return false;
        if (!name.StartsWith(prefix + "-", StringComparison.Ordinal)) return false;
        return name.AsSpan(prefix.Length + 1).ToString().All(char.IsAsciiHexDigit);
    }
}
