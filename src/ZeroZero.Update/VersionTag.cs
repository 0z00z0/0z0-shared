namespace ZeroZero.Update;

/// <summary>A release tag as a version: <c>v1.2.3</c> is 1.2.3.0.</summary>
public static class VersionTag
{
    /// <summary>Two to four numeric parts after an optional <c>v</c>. A pre-release suffix, a
    /// single number and anything else are refused rather than guessed at.</summary>
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        if (!Version.TryParse(NumberOf(tag), out Version? parsed)) return false;

        version = Normalise(parsed);
        return true;
    }

    /// <summary>The tag without its <c>v</c>, trimmed: what the installer file name carries.</summary>
    public static string NumberOf(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ReadOnlySpan<char> text = tag.AsSpan().Trim();
        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V')) text = text[1..];
        return text.ToString();
    }

    /// <summary>Four parts, absent ones zero. <see cref="Version"/> orders 1.2.3 before 1.2.3.0,
    /// which would report the running version out of date against its own tag.</summary>
    public static Version Normalise(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }
}
