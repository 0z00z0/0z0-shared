using ZeroZero.Primitives;

namespace ZeroZero.Update.Win32;

/// <summary>Whether an installer may start at this moment. The answer is about this moment and is
/// never kept: the attempt after it asks again.</summary>
/// <param name="Reason">Why not, in a few words. The log is keyed on it, so one wording per cause
/// keeps an hour of refusals to one line.</param>
public readonly record struct InstallMoment(bool Accepted, string Reason = "")
{
    /// <summary>Nothing is in the way.</summary>
    public static InstallMoment Now { get; } = new(true);

    public static InstallMoment NotNow(string reason) => new(false, reason);
}

/// <summary>What an application supplies to install updates without anyone accepting anything.
/// Absent, nothing of the kind happens.</summary>
public sealed class UnattendedUpdateOptions
{
    /// <summary>Whether updates install without anyone accepting anything. False unless the
    /// application sets it, so an absent setting installs nothing and checks nothing.</summary>
    public bool Enabled { get; init; }

    /// <summary>How long after <see cref="UnattendedUpdatePolicy.Start"/> the first tick runs.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long between one check and the next. Once a day unless the application sets
    /// it. Counted from process start, never persisted.</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How often the policy looks again between checks. A check that did not complete and
    /// an installer that could not start are both retried on this tick rather than waiting for the
    /// next check.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>What the application does once the installer is running: mark its exit deliberate
    /// and exit. Called after the installer process exists and never before.</summary>
    public required Action Shutdown { get; init; }

    /// <summary>Asked immediately before the installer starts, once the machine-free rule has
    /// already passed. A refusal stops that attempt and nothing else; the next tick asks again.
    /// Null accepts every moment.</summary>
    public Func<ReleaseInfo, InstallMoment>? MayInstallNow { get; init; }

    public ILogSink Log { get; init; } = NullLogSink.Instance;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Shutdown, nameof(Shutdown));
        if (InitialDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(InitialDelay), InitialDelay, "The initial delay cannot be negative.");
        if (CheckInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CheckInterval), CheckInterval, "The check interval must be longer than zero.");
        if (RetryInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RetryInterval), RetryInterval, "The retry interval must be longer than zero.");
    }
}
