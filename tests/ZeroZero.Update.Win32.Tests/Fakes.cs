using ZeroZero.Primitives;

namespace ZeroZero.Update.Win32.Tests;

internal sealed class RecordingLogSink : ILogSink
{
    private readonly Lock _lock = new();

    public List<string> Infos { get; } = [];
    public List<(string Source, Exception? Error)> Errors { get; } = [];

    public void Info(string message)
    {
        lock (_lock) Infos.Add(message);
    }

    public void Error(string source, Exception? ex)
    {
        lock (_lock) Errors.Add((source, ex));
    }
}

/// <summary>A service that answers what the test set and records what the flow asked of it. No
/// network, no file, no process.</summary>
internal sealed class FakeUpdateService : IUpdateService
{
    public static readonly ReleaseInfo Release = new(
        "v1.2.3", new Version(1, 2, 3, 0), "1.2.3", "Product v1.2.3",
        "## Product v1.2.3\n\n- a thing\n\n**SHA256 (installer):** `AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084`",
        new Uri("https://example.invalid/releases/tag/v1.2.3"), null,
        [new ReleaseAsset("Product-Setup-1.2.3.exe", 10, new Uri("https://example.invalid/download/Product-Setup-1.2.3.exe"))]);

    public Version RunningVersion { get; init; } = new(1, 0, 0, 0);

    public UpdateCheckResult CheckResult { get; set; } = new(UpdateCheckOutcome.UpToDate, new Version(1, 0, 0, 0), Release);

    /// <summary>Held until released, so a second run can be started while the first is inside the check.</summary>
    public TaskCompletionSource? HoldCheck { get; set; }

    public PreparedUpdate Prepared { get; set; } = Ready(Release);

    public LaunchResult LaunchResult { get; set; } = new(true, "the installer is running");

    public int Checks { get; private set; }
    public int Prepares { get; private set; }
    public int Launches { get; private set; }
    public List<string> Sequence { get; } = [];

    public static PreparedUpdate Ready(ReleaseInfo release) =>
        new(PrepareOutcome.Ready, release, "Product-Setup-1.2.3.exe", @"C:\downloads\Product-Setup-1.2.3.exe", "AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084",
            new VerificationResult(VerificationVerdict.Verified, "signed by the expected signer"), "verified");

    public static PreparedUpdate NotReady(PrepareOutcome outcome, VerificationVerdict? verdict = null) =>
        new(outcome, Release, "Product-Setup-1.2.3.exe", null, null,
            verdict is { } v ? new VerificationResult(v, "refused for the test's reason") : null, "not ready for the test's reason");

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        Checks++;
        Sequence.Add("check");
        if (HoldCheck is { } hold) await hold.Task.WaitAsync(cancellationToken);
        return CheckResult;
    }

    public Task<PreparedUpdate> PrepareAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Prepares++;
        Sequence.Add("prepare");
        return Task.FromResult(Prepared);
    }

    public LaunchResult Launch(PreparedUpdate update)
    {
        Launches++;
        Sequence.Add("launch");
        return LaunchResult;
    }

    public int SweepStaleDownloads(TimeSpan olderThan) => 0;
}

/// <summary>Prompts that answer what the test set and record what they were told.</summary>
internal sealed class RecordingPrompts : IUpdatePrompts
{
    public InstallChoice Choice { get; set; } = InstallChoice.Install;

    public List<ReleaseInfo> Asked { get; } = [];
    public List<Version> UpToDate { get; } = [];
    public int NothingReleased { get; private set; }
    public List<UpdateCheckResult> CheckFailed { get; } = [];
    public List<PreparedUpdate> CannotInstall { get; } = [];
    public List<(PreparedUpdate Update, LaunchResult Result)> LaunchFailed { get; } = [];
    public List<string> Sequence { get; } = [];

    public InstallChoice AskToInstall(ReleaseInfo release, Version runningVersion)
    {
        Asked.Add(release);
        Sequence.Add("ask");
        return Choice;
    }

    public void SayUpToDate(Version runningVersion) => UpToDate.Add(runningVersion);

    public void SayNothingReleased() => NothingReleased++;

    public void SayCheckFailed(UpdateCheckResult result) => CheckFailed.Add(result);

    public void SayCannotInstall(PreparedUpdate update) => CannotInstall.Add(update);

    public void SayLaunchFailed(PreparedUpdate update, LaunchResult result) => LaunchFailed.Add((update, result));

    public int Said => UpToDate.Count + NothingReleased + CheckFailed.Count + CannotInstall.Count + LaunchFailed.Count;
}
