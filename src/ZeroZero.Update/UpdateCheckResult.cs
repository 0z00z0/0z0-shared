namespace ZeroZero.Update;

/// <summary>What a check found. Only <see cref="UpdateAvailable"/> leads anywhere; the rest are
/// reported, or logged, and the next scheduled check runs as if nothing happened.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>The latest release is newer than the running version.</summary>
    UpdateAvailable,

    /// <summary>The latest release is the running version, or older.</summary>
    UpToDate,

    /// <summary>The repository has published no release. Not an error.</summary>
    NoReleases,

    /// <summary>GitHub refused the request under its rate limit; <see cref="UpdateCheckResult.RateLimitResetsAt"/> says when it lifts.</summary>
    RateLimited,

    /// <summary>The request could not be made or timed out.</summary>
    Unreachable,

    /// <summary>The service answered, and the answer is not a release this version understands.</summary>
    InvalidResponse,
}

public sealed record UpdateCheckResult(
    UpdateCheckOutcome Outcome,
    Version RunningVersion,
    ReleaseInfo? Release = null,
    DateTimeOffset? RateLimitResetsAt = null,
    string Detail = "",
    Exception? Error = null);

/// <summary>What the source answered, before the version comparison.</summary>
public enum ReleaseLookupOutcome
{
    Found,
    NoReleases,
    RateLimited,
    Unreachable,
    InvalidResponse,
}

public sealed record ReleaseLookup(
    ReleaseLookupOutcome Outcome,
    ReleaseInfo? Release = null,
    DateTimeOffset? RateLimitResetsAt = null,
    string Detail = "",
    Exception? Error = null);
