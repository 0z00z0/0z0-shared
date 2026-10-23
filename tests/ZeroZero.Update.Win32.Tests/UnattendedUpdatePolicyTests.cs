using Xunit;

namespace ZeroZero.Update.Win32.Tests;

/// <summary>The guards on installing without anyone accepting anything: the setting that has to be
/// on, the machine that has to be free, the moment an application may refuse, a check that found
/// nothing, the download an application may refuse, and a held release giving way only to a newer
/// one. Nothing here downloads, starts a process or reaches a screen.</summary>
public class UnattendedUpdatePolicyTests
{
    private readonly FakeUpdateService _service = new();
    private readonly FakeMachineIdle _idle = new();
    private readonly RecordingLogSink _log = new();
    private int _shutdowns;

    private UnattendedUpdatePolicy Policy(bool enabled = true, InstallHolds? holds = null, CheckCadence cadence = CheckCadence.Periodic,
        TimeSpan? initialDelay = null, TimeSpan? retryInterval = null, Action<UnattendedTick>? tickReported = null,
        Func<ReleaseInfo, InstallMoment>? mayDownloadNow = null, TimeProvider? time = null) =>
        new(_service, new UnattendedUpdateOptions
        {
            Enabled = enabled,
            Cadence = cadence,
            InitialDelay = initialDelay ?? TimeSpan.FromSeconds(30),
            RetryInterval = retryInterval ?? TimeSpan.FromMinutes(10),
            MayDownloadNow = mayDownloadNow,
            Holds = holds,
            TickReported = tickReported,
            Shutdown = () => _shutdowns++,
            Log = _log,
        }, _idle, time);

    /// <summary>Published after <see cref="FakeUpdateService.Release"/>, so strictly newer than it.</summary>
    private static readonly ReleaseInfo Newer = FakeUpdateService.Release with
    {
        TagName = "v1.3.0",
        Version = new Version(1, 3, 0, 0),
        VersionText = "1.3.0",
    };

    private void ReleaseIsWaiting(ReleaseInfo? release = null) =>
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 0, 0), release ?? FakeUpdateService.Release);

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task SettingOff_InstallsNothingAndChecksNothing()
    {
        ReleaseIsWaiting();
        _idle.ScreenLocked = true;

        UnattendedTick tick = await Policy(enabled: false).TickAsync();

        Assert.Equal(UnattendedOutcome.Disabled, tick.Outcome);
        Assert.Equal(0, _service.Checks);
        Assert.Equal(0, _service.Prepares);
        Assert.Equal(0, _service.Launches);
        Assert.Equal(0, _shutdowns);
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task MachineInUse_RefusesUntilTenMinutesHavePassed()
    {
        ReleaseIsWaiting();
        UnattendedUpdatePolicy policy = Policy();

        _idle.SinceLastInput = TimeSpan.FromMinutes(9);
        UnattendedTick refused = await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.Refused, refused.Outcome);
        Assert.Equal("the machine is in use", refused.Reason);
        Assert.Equal(0, _service.Launches);
        Assert.Equal(0, _shutdowns);

        // The other side of the boundary, so a refusal at nine minutes is the rule and not a
        // fixture that could never reach a launch.
        _idle.SinceLastInput = TimeSpan.FromMinutes(10);
        UnattendedTick started = await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.InstallerStarted, started.Outcome);
        Assert.Equal(1, _service.Launches);
        Assert.Equal(1, _shutdowns);
        // One download for both ticks: the verified installer is held across the refusal.
        Assert.Equal(1, _service.Prepares);

        // A tick landing while the application is still on its way out starts no second installer.
        UnattendedTick after = await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.InstallerStarted, after.Outcome);
        Assert.Equal(1, _service.Launches);
        Assert.Equal(1, _shutdowns);
    }

    /// <summary>Two direct calls overlapping inside the check start one installer: the second
    /// waits for the first and finds it handed over.</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task OverlappingTicks_StartOneInstaller()
    {
        ReleaseIsWaiting();
        _idle.ScreenLocked = true;
        _service.HoldCheck = new TaskCompletionSource();
        UnattendedUpdatePolicy policy = Policy();

        Task<UnattendedTick> first = policy.TickAsync();
        Task<UnattendedTick> second = policy.TickAsync();
        _service.HoldCheck.SetResult();
        UnattendedTick[] ticks = await Task.WhenAll(first, second);

        Assert.All(ticks, tick => Assert.Equal(UnattendedOutcome.InstallerStarted, tick.Outcome));
        Assert.Equal(1, _service.Launches);
        Assert.Equal(1, _shutdowns);
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task AHold_RefusesWithOneLogLine_AndInstallsOnceReleased()
    {
        ReleaseIsWaiting();
        _idle.ScreenLocked = true;
        var holds = new InstallHolds();
        IDisposable session = holds.Hold("a focus session is running");
        UnattendedUpdatePolicy policy = Policy(holds: holds);

        UnattendedTick refused = await policy.TickAsync();
        await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.Refused, refused.Outcome);
        Assert.Equal("a focus session is running", refused.Reason);
        Assert.Single(_log.Infos, line => line.StartsWith("Not installing", StringComparison.Ordinal));
        Assert.Equal(0, _service.Launches);
        Assert.Equal(0, _shutdowns);

        session.Dispose();
        UnattendedTick started = await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.InstallerStarted, started.Outcome);
        Assert.Equal(1, _service.Launches);
        Assert.Equal(1, _shutdowns);
    }

    /// <summary>An installer that will not start is removed, so the next tick's fresh download
    /// leaves no copy of it behind.</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task ALaunchThatFails_DiscardsTheInstaller()
    {
        ReleaseIsWaiting();
        _idle.ScreenLocked = true;
        _service.LaunchResult = new LaunchResult(false, "the installer could not be started");

        UnattendedTick tick = await Policy().TickAsync();

        Assert.Equal(UnattendedOutcome.LaunchFailed, tick.Outcome);
        Assert.Same(_service.Prepared, Assert.Single(_service.Discarded));
        Assert.Equal(0, _shutdowns);
    }

    /// <summary>The gate before the download: a refusal keeps the release found and downloads
    /// nothing, the refusal writes one line however many ticks repeat it, and the gate before the
    /// launch is never reached.</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task DownloadRefused_NothingIsDownloadedAndTheReleaseIsKept()
    {
        ReleaseIsWaiting();
        _idle.ScreenLocked = true;
        bool mayDownload = false;
        int installAsked = 0;
        var holds = new InstallHolds();
        holds.Hold(() =>
        {
            installAsked++;
            return null;
        });
        UnattendedUpdatePolicy policy = Policy(
            mayDownloadNow: _ => mayDownload ? InstallMoment.Now : InstallMoment.NotNow("installing is switched off"),
            holds: holds);

        UnattendedTick first = await policy.TickAsync();
        UnattendedTick second = await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.DownloadRefused, first.Outcome);
        Assert.Equal("installing is switched off", first.Reason);
        Assert.Equal(FakeUpdateService.Release, first.Release);
        Assert.Equal(UnattendedOutcome.DownloadRefused, second.Outcome);
        Assert.Equal(0, _service.Prepares);
        Assert.Equal(0, _service.Launches);
        Assert.Equal(0, installAsked);
        Assert.Single(_log.Infos, line => line.StartsWith("Not downloading", StringComparison.Ordinal));
        // Kept, not found again: the second tick checked nothing.
        Assert.Equal(1, _service.Checks);

        // The other side of the gate, so the refusal is the gate and not a fixture that could never
        // reach a download: the release kept from the first check downloads and starts.
        mayDownload = true;
        UnattendedTick third = await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.InstallerStarted, third.Outcome);
        Assert.Equal(1, _service.Prepares);
        Assert.Equal(1, _service.Checks);
        Assert.Equal(1, installAsked);
    }

    /// <summary>A release held without being downloaded does not stop checking: the next check
    /// comes on its cadence, not before, and a newer release it finds takes the held one's place.</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task ReleaseHeld_StillChecksOnCadence_AndFindsANewerOne()
    {
        var clock = new ManualClock();
        ReleaseIsWaiting();
        UnattendedUpdatePolicy policy = Policy(mayDownloadNow: _ => InstallMoment.NotNow("installing is switched off"), time: clock);

        UnattendedTick held = await policy.TickAsync();
        Assert.Equal(UnattendedOutcome.DownloadRefused, held.Outcome);
        Assert.Equal(1, _service.Checks);

        ReleaseIsWaiting(Newer);
        clock.Advance(TimeSpan.FromHours(23));
        UnattendedTick early = await policy.TickAsync();

        Assert.Equal(1, _service.Checks);
        Assert.Equal(FakeUpdateService.Release, early.Release);

        clock.Advance(TimeSpan.FromHours(1));
        UnattendedTick due = await policy.TickAsync();

        Assert.Equal(2, _service.Checks);
        Assert.Equal(UnattendedOutcome.DownloadRefused, due.Outcome);
        Assert.Equal(Newer, due.Release);
        Assert.Equal(0, _service.Prepares);
    }

    /// <summary>A verified installer held back from starting is kept through a check that fails and
    /// through a newer release that does not verify, and is replaced — and removed — only once a
    /// strictly newer one has been downloaded and verified. The newer one is then what starts.</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task InstallerHeld_IsReplacedOnlyByANewerOneThatVerified()
    {
        var clock = new ManualClock();
        ReleaseIsWaiting();
        _idle.ScreenLocked = true;
        bool mayInstall = false;
        var holds = new InstallHolds();
        holds.Hold(() => mayInstall ? null : "a job is running");
        UnattendedUpdatePolicy policy = Policy(holds: holds, time: clock);

        UnattendedTick first = await policy.TickAsync();
        Assert.Equal(UnattendedOutcome.Refused, first.Outcome);
        Assert.Equal(1, _service.Prepares);

        // A check that does not complete keeps the installer in hand.
        clock.Advance(TimeSpan.FromHours(24));
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.Unreachable, new Version(1, 0, 0, 0), Detail: "nothing answered");
        UnattendedTick failedCheck = await policy.TickAsync();

        Assert.Equal(2, _service.Checks);
        Assert.Equal(UnattendedOutcome.Refused, failedCheck.Outcome);
        Assert.Equal(FakeUpdateService.Release, failedCheck.Release);
        Assert.Empty(_service.Discarded);

        // So does a newer release that does not verify. The failed check stamped nothing, so this
        // tick checks again.
        ReleaseIsWaiting(Newer);
        _service.Prepared = FakeUpdateService.NotReady(PrepareOutcome.Refused, VerificationVerdict.HashMismatch) with { Release = Newer };
        UnattendedTick failedReplacement = await policy.TickAsync();

        Assert.Equal(3, _service.Checks);
        Assert.Equal(2, _service.Prepares);
        Assert.Equal(UnattendedOutcome.Refused, failedReplacement.Outcome);
        Assert.Equal(FakeUpdateService.Release, failedReplacement.Release);
        Assert.Empty(_service.Discarded);

        // A newer release that verifies takes its place, and the installer it replaces is removed.
        clock.Advance(TimeSpan.FromHours(24));
        _service.Prepared = FakeUpdateService.Ready(Newer);
        UnattendedTick replaced = await policy.TickAsync();

        Assert.Equal(4, _service.Checks);
        Assert.Equal(3, _service.Prepares);
        Assert.Equal(UnattendedOutcome.Refused, replaced.Outcome);
        Assert.Equal(Newer, replaced.Release);
        PreparedUpdate discarded = Assert.Single(_service.Discarded);
        Assert.Equal(FakeUpdateService.Release, discarded.Release);

        mayInstall = true;
        UnattendedTick started = await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.InstallerStarted, started.Outcome);
        Assert.Equal(Newer, started.Release);
        Assert.Equal(1, _service.Launches);
        Assert.Equal(1, _shutdowns);
    }

    [Fact]
    public async Task CheckFoundNothing_NothingIsDownloadedAndNothingStarts()
    {
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.UpToDate, new Version(1, 0, 0, 0), FakeUpdateService.Release);
        _idle.ScreenLocked = true;

        UnattendedTick tick = await Policy().TickAsync();

        Assert.Equal(UnattendedOutcome.NothingToInstall, tick.Outcome);
        Assert.Equal(1, _service.Checks);
        Assert.Equal(0, _service.Prepares);
        Assert.Equal(0, _service.Launches);
        Assert.Equal(0, _shutdowns);
    }

    /// <summary>The guard on <see cref="CheckCadence.Once"/>: the single check is spent whatever it
    /// finds, a failure included, so a later tick never checks again — the one thing a periodic
    /// interval cannot express at any value.</summary>
    [Fact]
    public async Task CheckCadenceOnce_NoLaterTickChecksAgain_EvenAfterACheckThatFailed()
    {
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.Unreachable, new Version(1, 0, 0, 0));
        UnattendedUpdatePolicy policy = Policy(cadence: CheckCadence.Once);

        UnattendedTick first = await policy.TickAsync();
        Assert.Equal(UnattendedOutcome.CheckFailed, first.Outcome);
        Assert.Equal(1, _service.Checks);

        UnattendedTick second = await policy.TickAsync();
        Assert.Equal(UnattendedOutcome.NotDue, second.Outcome);
        Assert.Equal(1, _service.Checks);
    }

    /// <summary>A failed download does not end unattended updating, even with no check left to find
    /// the release again; a download that did not verify is not fetched again every tick.</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData(PrepareOutcome.DownloadFailed, true)]
    [InlineData(PrepareOutcome.Refused, false)]
    public async Task NotPrepared_UnderOnce_OnlyAFailedDownloadIsTriedAgain(PrepareOutcome outcome, bool triedAgain)
    {
        ReleaseIsWaiting();
        _idle.ScreenLocked = true;
        _service.Prepared = FakeUpdateService.NotReady(outcome, outcome == PrepareOutcome.Refused ? VerificationVerdict.HashMismatch : null);
        UnattendedUpdatePolicy policy = Policy(cadence: CheckCadence.Once);

        UnattendedTick failed = await policy.TickAsync();
        Assert.Equal(UnattendedOutcome.NotPrepared, failed.Outcome);

        _service.Prepared = FakeUpdateService.Ready(FakeUpdateService.Release);
        UnattendedTick next = await policy.TickAsync();

        Assert.Equal(triedAgain ? UnattendedOutcome.InstallerStarted : UnattendedOutcome.NotDue, next.Outcome);
        Assert.Equal(triedAgain ? 2 : 1, _service.Prepares);
        Assert.Equal(1, _service.Checks);
    }

    /// <summary>The gap <c>TickReported</c> closes: a policy driving itself through <see
    /// cref="UnattendedUpdatePolicy.Start"/> hands every tick's result to the callback, because the
    /// scheduler calls <see cref="UnattendedUpdatePolicy.TickAsync"/> internally and otherwise keeps
    /// it to itself. A callback that throws must not take the tick, or the policy, down with it — the
    /// direct call below bypasses the scheduler's own resilience so this is the policy's own guard
    /// being tested, not the scheduler's.</summary>
    [Fact]
    public async Task TickReported_SeesASelfDrivenTick_AndACallbackThatThrowsDoesNotStopThePolicy()
    {
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.UpToDate, new Version(1, 0, 0, 0), FakeUpdateService.Release);
        var seenByScheduler = new List<UnattendedTick>();

        using (UnattendedUpdatePolicy driven = Policy(
            initialDelay: TimeSpan.Zero,
            retryInterval: TimeSpan.FromMilliseconds(20),
            tickReported: tick => { lock (seenByScheduler) seenByScheduler.Add(tick); }))
        {
            driven.Start();
            await Polling.WaitUntil(() => { lock (seenByScheduler) return seenByScheduler.Count > 0; }, TimeSpan.FromSeconds(5));
        }

        UnattendedTick first;
        lock (seenByScheduler) first = seenByScheduler[0];
        Assert.Equal(UnattendedOutcome.NothingToInstall, first.Outcome);

        int calls = 0;
        using UnattendedUpdatePolicy direct = Policy(tickReported: _ =>
        {
            calls++;
            throw new InvalidOperationException("the callback's own fault");
        });

        UnattendedTick tick = await direct.TickAsync();

        Assert.Equal(UnattendedOutcome.NothingToInstall, tick.Outcome);
        Assert.Equal(1, calls);
        (string source, Exception? error) = Assert.Single(_log.Errors);
        Assert.Equal(nameof(UnattendedUpdatePolicy), source);
        Assert.IsType<InvalidOperationException>(error);

        // The policy itself is unharmed: a second tick still runs and is still reported.
        UnattendedTick second = await direct.TickAsync();
        Assert.Equal(UnattendedOutcome.NotDue, second.Outcome);
        Assert.Equal(2, calls);
    }
}
