using Xunit;

namespace ZeroZero.Update.Win32.Tests;

/// <summary>The orchestration over a recording service and recording prompts: what is asked,
/// what is said, in which order, and above all that the shutdown callback runs after the
/// installer has started and never otherwise.</summary>
public class UpdateFlowTests
{
    private readonly FakeUpdateService _service = new();
    private readonly RecordingPrompts _prompts = new();
    private readonly RecordingLogSink _log = new();
    private readonly List<Uri> _opened = [];
    private int _shutdowns;

    private UpdateFlow Flow() => new(_service, _prompts, new UpdateFlowOptions
    {
        Shutdown = () =>
        {
            _shutdowns++;
            // On the service's own sequence, so its place among check, prepare and launch is seen.
            _service.Sequence.Add("shutdown");
        },
        OpenReleasePage = _opened.Add,
        Log = _log,
    });

    private static UpdateCheckResult Available() =>
        new(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 0, 0), FakeUpdateService.Release);

    [Fact]
    public async Task ManualRun_SaysUpToDate()
    {
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.UpToDate, new Version(1, 0, 0, 0), FakeUpdateService.Release);

        UpdateFlowResult result = await Flow().RunAsync(UpdateTrigger.Manual);

        Assert.Equal(UpdateFlowResult.UpToDate, result);
        Assert.Equal([new Version(1, 0, 0, 0)], _prompts.UpToDate);
        Assert.Empty(_prompts.Asked);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task ScheduledRun_SaysNothingWhenUpToDate()
    {
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.UpToDate, new Version(1, 0, 0, 0), FakeUpdateService.Release);

        UpdateFlowResult result = await Flow().RunAsync(UpdateTrigger.Scheduled);

        Assert.Equal(UpdateFlowResult.UpToDate, result);
        Assert.Equal(0, _prompts.Said);
    }

    [Fact]
    public async Task ManualRun_SaysNothingHasBeenReleased()
    {
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.NoReleases, new Version(1, 0, 0, 0));

        UpdateFlowResult result = await Flow().RunAsync(UpdateTrigger.Manual);

        Assert.Equal(UpdateFlowResult.NothingReleased, result);
        Assert.Equal(1, _prompts.NothingReleased);
    }

    [Theory]
    [InlineData(UpdateCheckOutcome.RateLimited)]
    [InlineData(UpdateCheckOutcome.Unreachable)]
    [InlineData(UpdateCheckOutcome.InvalidResponse)]
    public async Task ManualRun_ReportsACheckThatFailed(UpdateCheckOutcome outcome)
    {
        _service.CheckResult = new UpdateCheckResult(outcome, new Version(1, 0, 0, 0), Detail: "why");

        UpdateFlowResult result = await Flow().RunAsync(UpdateTrigger.Manual);

        Assert.Equal(UpdateFlowResult.CheckFailed, result);
        UpdateCheckResult said = Assert.Single(_prompts.CheckFailed);
        Assert.Equal(outcome, said.Outcome);
        Assert.Empty(_prompts.Asked);
    }

    [Theory]
    [InlineData(UpdateCheckOutcome.RateLimited)]
    [InlineData(UpdateCheckOutcome.Unreachable)]
    [InlineData(UpdateCheckOutcome.InvalidResponse)]
    [InlineData(UpdateCheckOutcome.NoReleases)]
    public async Task ScheduledRun_KeepsACheckThatFailedToItself(UpdateCheckOutcome outcome)
    {
        _service.CheckResult = new UpdateCheckResult(outcome, new Version(1, 0, 0, 0), Detail: "why");

        await Flow().RunAsync(UpdateTrigger.Scheduled);

        Assert.Equal(0, _prompts.Said);
        Assert.Equal(0, _service.Prepares);
    }

    [Theory]
    [InlineData(UpdateTrigger.Manual)]
    [InlineData(UpdateTrigger.Scheduled)]
    public async Task AnAvailableUpdate_IsOfferedWhoeverStartedTheRun(UpdateTrigger trigger)
    {
        _service.CheckResult = Available();
        _prompts.Choice = InstallChoice.Later;

        UpdateFlowResult result = await Flow().RunAsync(trigger);

        Assert.Equal(UpdateFlowResult.Declined, result);
        ReleaseInfo asked = Assert.Single(_prompts.Asked);
        Assert.Equal("v1.2.3", asked.TagName);
        Assert.Equal(0, _service.Prepares);
        Assert.Equal(0, _shutdowns);
        Assert.Contains(_log.Infos, line => line.Contains("declined"));
    }

    [Fact]
    public async Task OpeningTheReleasePage_OpensItAndDownloadsNothing()
    {
        _service.CheckResult = Available();
        _prompts.Choice = InstallChoice.OpenReleasePage;

        UpdateFlowResult result = await Flow().RunAsync(UpdateTrigger.Manual);

        Assert.Equal(UpdateFlowResult.ReleasePageOpened, result);
        Assert.Equal([FakeUpdateService.Release.HtmlUri!], _opened);
        Assert.Equal(0, _service.Prepares);
    }

    [Fact]
    public async Task OpeningTheReleasePage_RefusesAnythingButHttps()
    {
        ReleaseInfo release = FakeUpdateService.Release with { HtmlUri = new Uri("http://example.invalid/releases") };
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 0, 0), release);
        _prompts.Choice = InstallChoice.OpenReleasePage;

        await Flow().RunAsync(UpdateTrigger.Manual);

        Assert.Empty(_opened);
        Assert.Contains(_log.Infos, line => line.Contains("not https"));
    }

    [Fact]
    public async Task Installing_PreparesLaunchesAndThenShutsDown()
    {
        _service.CheckResult = Available();
        _service.Sequence.Clear();

        UpdateFlowResult result = await Flow().RunAsync(UpdateTrigger.Manual);

        Assert.Equal(UpdateFlowResult.InstallerStarted, result);
        Assert.Equal(1, _service.Prepares);
        Assert.Equal(1, _service.Launches);
        Assert.Equal(1, _shutdowns);
        // The application exits only once the installer process exists.
        Assert.Equal(["check", "prepare", "launch", "shutdown"], _service.Sequence);
        Assert.Equal(0, _prompts.Said);
    }

    [Theory]
    [InlineData(PrepareOutcome.Refused, VerificationVerdict.HashMismatch)]
    [InlineData(PrepareOutcome.Refused, VerificationVerdict.SignerMismatch)]
    [InlineData(PrepareOutcome.Refused, VerificationVerdict.NotSigned)]
    [InlineData(PrepareOutcome.Refused, VerificationVerdict.CertificateNotPinned)]
    [InlineData(PrepareOutcome.HashNotPublished, null)]
    [InlineData(PrepareOutcome.HashAmbiguous, null)]
    [InlineData(PrepareOutcome.InstallerAssetMissing, null)]
    [InlineData(PrepareOutcome.DownloadFailed, null)]
    public async Task AnUpdateThatIsNotReady_IsReportedAndNeverLaunched(PrepareOutcome outcome, VerificationVerdict? verdict)
    {
        _service.CheckResult = Available();
        _service.Prepared = FakeUpdateService.NotReady(outcome, verdict);

        UpdateFlowResult result = await Flow().RunAsync(UpdateTrigger.Scheduled);

        Assert.Equal(UpdateFlowResult.CannotInstall, result);
        PreparedUpdate said = Assert.Single(_prompts.CannotInstall);
        Assert.Equal(outcome, said.Outcome);
        Assert.Equal(0, _service.Launches);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task AReadyUpdateWithoutAVerifiedVerdict_IsNotLaunched()
    {
        // Defence in depth: the outcome says ready and the verification says otherwise.
        _service.CheckResult = Available();
        _service.Prepared = FakeUpdateService.Ready(FakeUpdateService.Release) with
        {
            Verification = new VerificationResult(VerificationVerdict.SignerMismatch, "not the signer"),
        };

        UpdateFlowResult result = await Flow().RunAsync(UpdateTrigger.Manual);

        Assert.Equal(UpdateFlowResult.CannotInstall, result);
        Assert.Equal(0, _service.Launches);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task ALaunchThatFails_IsReportedAndTheApplicationStaysUp()
    {
        _service.CheckResult = Available();
        _service.LaunchResult = new LaunchResult(false, "refused at launch: the file changed");

        UpdateFlowResult result = await Flow().RunAsync(UpdateTrigger.Manual);

        Assert.Equal(UpdateFlowResult.LaunchFailed, result);
        (PreparedUpdate _, LaunchResult said) = Assert.Single(_prompts.LaunchFailed);
        Assert.Contains("refused at launch", said.Detail);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task ASecondRun_BacksOffWhileTheFirstIsInProgress()
    {
        _service.CheckResult = Available();
        _service.HoldCheck = new TaskCompletionSource();
        _prompts.Choice = InstallChoice.Later;
        UpdateFlow flow = Flow();

        Task<UpdateFlowResult> first = flow.RunAsync(UpdateTrigger.Scheduled);
        Task<UpdateFlowResult> second = flow.RunAsync(UpdateTrigger.Manual);

        // The second run backs off at once; it does not queue behind the first.
        Assert.True(second.IsCompleted);
        Assert.Equal(UpdateFlowResult.AlreadyRunning, await second);
        _service.HoldCheck.SetResult();
        Assert.Equal(UpdateFlowResult.Declined, await first);
        Assert.Equal(1, _service.Checks);

        // And the gate opens again once the first run is over.
        Assert.Equal(UpdateFlowResult.Declined, await flow.RunAsync(UpdateTrigger.Manual));
        Assert.Equal(2, _service.Checks);
    }

    [Fact]
    public async Task CancellationReachesTheService()
    {
        _service.HoldCheck = new TaskCompletionSource();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Flow().RunAsync(UpdateTrigger.Manual, cancel.Token));

        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public void Construction_NeedsAShutdownCallback()
    {
        Assert.Throws<ArgumentNullException>(() => new UpdateFlow(_service, _prompts, new UpdateFlowOptions { Shutdown = null! }));
    }
}
