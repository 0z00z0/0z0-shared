using Xunit;

namespace ZeroZero.Update.Win32.Tests;

/// <summary>The four guards on installing without anyone accepting anything: the setting that has
/// to be on, the machine that has to be free, the moment an application may refuse, and a check
/// that found nothing. Nothing here downloads, starts a process or reaches a screen.</summary>
public class UnattendedUpdatePolicyTests
{
    private readonly FakeUpdateService _service = new();
    private readonly FakeMachineIdle _idle = new();
    private readonly RecordingLogSink _log = new();
    private int _shutdowns;

    private UnattendedUpdatePolicy Policy(bool enabled = true, Func<ReleaseInfo, InstallMoment>? mayInstallNow = null, CheckCadence cadence = CheckCadence.Periodic,
        TimeSpan? initialDelay = null, TimeSpan? retryInterval = null, Action<UnattendedTick>? tickReported = null) =>
        new(_service, new UnattendedUpdateOptions
        {
            Enabled = enabled,
            Cadence = cadence,
            InitialDelay = initialDelay ?? TimeSpan.FromSeconds(30),
            RetryInterval = retryInterval ?? TimeSpan.FromMinutes(10),
            MayInstallNow = mayInstallNow,
            TickReported = tickReported,
            Shutdown = () => _shutdowns++,
            Log = _log,
        }, _idle);

    private void ReleaseIsWaiting() =>
        _service.CheckResult = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(1, 0, 0, 0), FakeUpdateService.Release);

    private static async Task WaitUntil(Func<bool> condition, TimeSpan within)
    {
        DateTime deadline = DateTime.UtcNow + within;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in time.");
            await Task.Delay(10);
        }
    }

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
        Assert.Equal(UnattendedUpdatePolicy.MachineInUse, refused.Reason);
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

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task ApplicationRefusesTheMoment_NothingStarts()
    {
        ReleaseIsWaiting();
        _idle.ScreenLocked = true;
        var asked = new List<ReleaseInfo>();

        UnattendedUpdatePolicy policy = Policy(mayInstallNow: release =>
        {
            asked.Add(release);
            return InstallMoment.NotNow("the application is in the middle of something");
        });

        UnattendedTick refused = await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.Refused, refused.Outcome);
        Assert.Equal("the application is in the middle of something", refused.Reason);
        Assert.Equal([FakeUpdateService.Release], asked);
        Assert.Equal(0, _service.Launches);
        Assert.Equal(0, _shutdowns);
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
            await WaitUntil(() => { lock (seenByScheduler) return seenByScheduler.Count > 0; }, TimeSpan.FromSeconds(5));
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
