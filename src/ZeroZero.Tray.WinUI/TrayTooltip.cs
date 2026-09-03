using System.Text;

namespace ZeroZero.Tray.WinUI;

/// <summary>
/// The tooltip discipline: blank lines dropped, a line repeating an earlier one dropped, and the
/// whole held to the shell's limit. The limit is 127 UTF-16 units — the notify-icon structure's
/// tip is 128 including its terminator — and a cut never lands between the halves of a surrogate
/// pair, which the shell would otherwise render as a broken character. A line that does not fit
/// is cut before its suffix, with an ellipsis, so the number on the line survives the name.
/// </summary>
public static class TrayTooltip
{
    /// <summary>The most the shell shows.</summary>
    public const int MaxUnits = 127;

    private const char Ellipsis = '…';

    /// <summary>The tooltip for plain lines, none protected.</summary>
    public static string Compose(params string?[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return Compose(lines.Select(l => new TrayTooltipLine(l)));
    }

    /// <summary>The tooltip for the given lines, applying the discipline above in order: a line
    /// that fits is taken whole, a line that does not is cut before its suffix, and once nothing
    /// more fits the rest is dropped.</summary>
    public static string Compose(IEnumerable<TrayTooltipLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var text = new StringBuilder(MaxUnits);

        foreach (var line in lines)
        {
            string body = (line.Text ?? "").Trim();
            string suffix = line.Suffix ?? "";
            if (body.Length == 0 && suffix.Trim().Length == 0) continue;

            string whole = body + suffix;
            if (!seen.Add(whole)) continue;

            int budget = MaxUnits - text.Length - (text.Length > 0 ? 1 : 0);
            string fitted = Fit(body, suffix, budget);
            if (fitted.Length == 0) break;

            if (text.Length > 0) text.Append('\n');
            text.Append(fitted);
        }

        return text.ToString();
    }

    /// <summary>The line as it fits in <paramref name="budget"/> units, or empty when even a cut
    /// line would carry nothing of the body but the ellipsis.</summary>
    private static string Fit(string body, string suffix, int budget)
    {
        if (body.Length + suffix.Length <= budget) return body + suffix;

        // Room for at least one unit of the body, the ellipsis and the whole suffix: a suffix
        // alone says nothing, and a suffix that itself exceeds the budget cannot be protected.
        int keep = budget - suffix.Length - 1;
        if (keep < 1) return "";

        if (char.IsHighSurrogate(body[keep - 1])) keep--;
        if (keep < 1) return "";

        return string.Concat(body.AsSpan(0, keep).TrimEnd(), Ellipsis.ToString(), suffix);
    }
}
