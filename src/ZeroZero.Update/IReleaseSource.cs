namespace ZeroZero.Update;

/// <summary>Where the latest release comes from. GitHub's releases API in the application; a fake
/// or a loopback server in a test.</summary>
public interface IReleaseSource
{
    Task<ReleaseLookup> LookupLatestAsync(CancellationToken cancellationToken = default);
}
