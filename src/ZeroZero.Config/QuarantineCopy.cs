using System.Globalization;
using System.Text.RegularExpressions;

namespace ZeroZero.Config;

/// <summary>Preserves a settings file that cannot be parsed as a stamped copy, written from bytes
/// already in hand, and prunes older copies. Naming and pruning rules: docs/zerozero-config.md.</summary>
public static class QuarantineCopy
{
    private const string Marker = ".bad";
    private const string StampFormat = "yyyy-MM-dd-HHmmss";

    // The stamp StampFormat writes, and the attempt number a second copy in the same second takes.
    private const string StampPattern = @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9]{6}(-[0-9]+)?\z";

    /// <summary>Writes a new copy of <paramref name="fileName"/> holding <paramref name="content"/> into the
    /// policy's folder, or <paramref name="directory"/> when it names none, and prunes beyond <c>Keep</c>.
    /// Returns the copy's path; null when <c>Keep</c> is zero or the copy was refused. Never throws for a refusal.</summary>
    public static string? TryWrite(
        SettingsFileQuarantine policy,
        string directory,
        string fileName,
        ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (policy.Keep <= 0) return null;

        var folder = policy.Directory ?? directory;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        string copy;

        try
        {
            Directory.CreateDirectory(folder);

            var stamp = DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture);
            copy = Path.Combine(folder, $"{stem}.{stamp}{Marker}{extension}");
            for (var attempt = 2; File.Exists(copy); attempt++)
            {
                copy = Path.Combine(folder, $"{stem}.{stamp}-{attempt}{Marker}{extension}");
            }
        }
        catch (Exception ex) when (AtomicFile.IsFileFailure(ex))
        {
            return null;
        }

        if (AtomicFile.Write(copy, content) is not null) return null;

        Prune(folder, stem, extension, policy.Keep);
        return copy;
    }

    // A failed prune leaves one copy too many, which costs nothing; it never reports the copy as failed.
    private static void Prune(string folder, string stem, string extension, int keep)
    {
        try
        {
            // The stamp sorts chronologically, so the newest copies are the last by ordinal name.
            var stale = Directory.EnumerateFiles(folder, $"{stem}.*{Marker}{extension}")
                .Where(path => IsCopyOf(path, stem, extension))
                .OrderDescending(StringComparer.Ordinal)
                .Skip(keep)
                .ToList();

            foreach (var path in stale) AtomicFile.TryDelete(path);
        }
        catch (Exception ex) when (AtomicFile.IsFileFailure(ex))
        {
            // Deliberately unreported.
        }
    }

    // The search pattern alone also takes in the copies of a file whose name starts with this stem
    // and a dot (app.local.json beside app.json), and the prune would delete them.
    private static bool IsCopyOf(string path, string stem, string extension)
    {
        var name = Path.GetFileName(path);
        var head = stem.Length + 1;
        var tail = Marker.Length + extension.Length;

        return name.Length > head + tail
            && name.StartsWith(stem + ".", StringComparison.Ordinal)
            && name.EndsWith(Marker + extension, StringComparison.Ordinal)
            && Regex.IsMatch(name.AsSpan(head, name.Length - head - tail), StampPattern);
    }
}
