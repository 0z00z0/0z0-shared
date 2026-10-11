using System.Globalization;

namespace ZeroZero.Primitives;

/// <summary>A file of moments, one per line in the invariant round-trip ("O") form; a limiter's
/// count kept on disk because the process counting is the one that keeps dying.</summary>
public static class TimestampListFile
{
    private const string Format = "O";

    /// <summary>The moments in file order; none for a missing file. A line that does not parse is
    /// dropped. A file that cannot be read throws the file system's exception.</summary>
    public static IReadOnlyList<DateTimeOffset> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return [];

        var stamps = new List<DateTimeOffset>();
        foreach (string line in File.ReadAllLines(path))
            if (DateTimeOffset.TryParseExact(line.Trim(), Format, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset stamp))
                stamps.Add(stamp);
        return stamps;
    }

    /// <summary>Replaces the file, creating its folder; a failed write throws the file system's exception.</summary>
    public static void Write(string path, IEnumerable<DateTimeOffset> stamps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(stamps);

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
        File.WriteAllLines(path, stamps.Select(stamp => stamp.ToString(Format, CultureInfo.InvariantCulture)));
    }
}
