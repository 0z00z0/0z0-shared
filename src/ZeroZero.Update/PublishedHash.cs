using System.Text.RegularExpressions;

namespace ZeroZero.Update;

public enum PublishedHashOutcome
{
    Found,

    /// <summary>The release notes carry no SHA-256. Nothing is downloaded: a file that cannot be
    /// verified is not fetched.</summary>
    NotPublished,

    /// <summary>The release notes carry more than one distinct SHA-256, so which one is the
    /// installer's is a guess, and a guess is refused.</summary>
    Ambiguous,
}

/// <summary>The SHA-256 a release publishes for its installer, read from the release body — the
/// text the release JSON already carries, so the hash is reachable exactly when the release is,
/// with no second request. The body is written by the release workflow that attaches the
/// installer, so the hash answers whether the download is whole, and only the signature answers
/// whether it is the publisher's.</summary>
public sealed partial record PublishedHash(PublishedHashOutcome Outcome, string? Sha256Hex)
{
    [GeneratedRegex("(?<![0-9A-Za-z])[0-9A-Fa-f]{64}(?![0-9A-Za-z])")]
    private static partial Regex Sha256Token();

    public static PublishedHash FromBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return new(PublishedHashOutcome.NotPublished, null);

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Sha256Token().Matches(body))
            found.Add(match.Value.ToUpperInvariant());

        return found.Count switch
        {
            0 => new(PublishedHashOutcome.NotPublished, null),
            1 => new(PublishedHashOutcome.Found, found.First()),
            _ => new(PublishedHashOutcome.Ambiguous, null),
        };
    }

    /// <summary>Whether a line of the notes carries a SHA-256, so the notes shown in a dialog can
    /// leave it out.</summary>
    public static bool IsHashLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return Sha256Token().IsMatch(line);
    }
}
