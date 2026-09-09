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

    /// <summary>Nothing answered: the name did not resolve, the connection was refused, there is no
    /// route, or the answer stopped mid-way. <see cref="TimedOut"/> is the neighbouring case, where
    /// something is at that address and did not answer in time.</summary>
    Unreachable,

    /// <summary>The service answered, and the answer is not a release this version understands: not
    /// JSON, not a release object, no tag, or a tag that is not a version.</summary>
    InvalidResponse,

    // The two below are appended rather than filed beside the outcomes they split from, so the
    // members above keep the numbers they already had.

    /// <summary>Something is at that address and it did not answer within
    /// <see cref="UpdateOptions.RequestTimeout"/>. A cancellation the caller asked for is never
    /// this: it leaves the check as an <see cref="OperationCanceledException"/> and no outcome.</summary>
    TimedOut,

    /// <summary>The service answered, and its answer is a failure status rather than a release. The
    /// request reached the service, which is what separates this from <see cref="Unreachable"/> and
    /// <see cref="TimedOut"/>; a refusal under the rate limit is <see cref="RateLimited"/> and never
    /// this.</summary>
    RequestFailed,
}

public sealed record UpdateCheckResult(
    UpdateCheckOutcome Outcome,
    Version RunningVersion,
    ReleaseInfo? Release = null,
    DateTimeOffset? RateLimitResetsAt = null,
    string Detail = "",
    Exception? Error = null);

/// <summary>What the source answered, before the version comparison. One member per member of
/// <see cref="UpdateCheckOutcome"/> that a lookup can produce, and mapped by name.</summary>
public enum ReleaseLookupOutcome
{
    Found,
    NoReleases,
    RateLimited,
    Unreachable,
    InvalidResponse,
    TimedOut,
    RequestFailed,
}

public sealed record ReleaseLookup(
    ReleaseLookupOutcome Outcome,
    ReleaseInfo? Release = null,
    DateTimeOffset? RateLimitResetsAt = null,
    string Detail = "",
    Exception? Error = null);
