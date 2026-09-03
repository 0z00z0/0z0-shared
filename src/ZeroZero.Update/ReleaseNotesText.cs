using System.Text.RegularExpressions;

namespace ZeroZero.Update;

/// <summary>Release notes as a dialog shows them: the markdown taken off, the hash line left out.</summary>
public static partial class ReleaseNotesText
{
    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+")]
    private static partial Regex HeadingMarker();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^(\s*)[-*+]\s+")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"(?<![\w*])[*_]([^*_\s][^*_]*?)[*_](?![\w*])")]
    private static partial Regex Emphasis();

    public static string Strip(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        var lines = new List<string>();
        foreach (string raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = raw.TrimEnd();
            if (PublishedHash.IsHashLine(line)) continue;

            line = HeadingMarker().Replace(line, "");
            line = ListMarker().Replace(line, "$1• ");
            line = Link().Replace(line, "$1");
            line = line.Replace("**", "", StringComparison.Ordinal).Replace("__", "", StringComparison.Ordinal);
            line = Emphasis().Replace(line, "$1");
            line = line.Replace("`", "", StringComparison.Ordinal);

            // One blank line at most between paragraphs, and none at either end.
            if (line.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0)) continue;
            lines.Add(line);
        }

        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join(Environment.NewLine, lines);
    }
}
