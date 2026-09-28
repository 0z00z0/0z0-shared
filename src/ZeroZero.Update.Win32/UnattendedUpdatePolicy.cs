using ZeroZero.Primitives;

namespace ZeroZero.Update.Win32;

/// <summary>What one tick of the policy did.</summary>
public enum UnattendedOutcome
{
    /// <summary>The application has not switched unattended installing on. Nothing ran and nothing
    /// was checked.</summary>
    Disabled,

    /// <summary>The next check is not due and nothing is waiting to install.</summary>
    NotDue,

    /// <summary>The check did not complete. The cadence is not stamped, so the next tick checks
    /// again rather than waiting for the next day.</summary>
    CheckFailed,

    /// <summary>The check completed and there is nothing newer to install.</summary>
    NothingToInstall,

    /// <summary>The release was not downloaded, or did not verify. Nothing ran, and the next check
    /// decides again.</summary>
    NotPrepared,

    /// <summary>A verified installer is in hand and this moment was refused. Nothing ran; the next
    /// tick asks again.</summary>
    Refused,

    /// <summary>The installer did not start. The next tick prepares it again.</summary>
    LaunchFailed,

    /// <summary>The installer is running and the application has been told to exit. Every tick
    /// after it answers the same and does nothing, so no second installer is started.</summary>
    InstallerStarted,
}

/// <param name="Reason">Why the tick ended where it did, in a few words. Empty where there is
/// nothing to say.</param>
public sealed record UnattendedTick(UnattendedOutcome Outcome, string Reason = "", ReleaseInfo? Release = null);

/// <summary>
/// Decides when an update is checked for, and when an installer may start with nobody accepting
/// anything. Drives the check, the download and the launch; shows nothing at any point.
/// </summary>
/// <remarks>
/// The scheduler ticks at <see cref="UnattendedUpdateOptions.RetryInterval"/> and each tick decides
/// what is due, so one short tick serves both a check on its cadence and a retry of what did not
/// finish. A verified installer is held between ticks, so a refused moment costs no second
/// download. <see cref="CheckCadence"/> governs checking only: under <see cref="CheckCadence.Once"/>
/// the ticks go on for as long as the scheduler runs, retrying a moment that was refused or an
/// installer that could not start, and simply find no check due once the one check has run.
/// </remarks>
public sealed class UnattendedUpdatePolicy : IDisposable
{
    /// <summary>How long the machine must have gone untouched before an installer may start, where
    /// the screen is not locked. Fixed: an application refuses a moment and cannot permit one.</summary>
    public static readonly TimeSpan RequiredIdle = TimeSpan.FromMinutes(10);

    /// <summary>The reason a tick gives when the machine is neither locked nor untouched for long
    /// enough. One constant wording, so a machine in use all afternoon writes one line.</summary>
    public const string MachineInUse = "the machine is in use";

    private readonly IUpdateService _service;
    private readonly UpdateFlow _flow;
    private readonly UnattendedUpdateOptions _options;
    private readonly IMachineIdle _idle;
    private readonly TimeProvider _time;
    private readonly ILogSink _log;
    private readonly UpdateScheduler? _scheduler;
    private DateTimeOffset? _checkedAt;
    private ReleaseInfo? _release;
    private PreparedUpdate? _prepared;
    private string? _refusal;
    private bool _handedOver;

    // Set the moment the one check under CheckCadence.Once is attempted, before its outcome is
    // known: "no further check, whatever happens" has to cover a failure, not just a success.
    private bool _onceChecked;

    /// <param name="idle">What reads the machine; the session this process runs in when null.</param>
    /// <param name="time">Where the cadence reads the clock; the system clock when null.</param>
    public UnattendedUpdatePolicy(IUpdateService service, UnattendedUpdateOptions options, IMachineIdle? idle = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _service = service;
        _options = options;
        _idle = idle ?? MachineIdle.Instance;
        _time = time ?? TimeProvider.System;
        _log = options.Log;

        // Its own flow, wired to prompts that answer themselves, so nothing on this path can draw
        // over the application's own update window.
        _flow = new UpdateFlow(service, SilentUpdatePrompts.Instance, new UpdateFlowOptions
        {
            Shutdown = options.Shutdown,
            Log = options.Log,
        });

        _scheduler = options.Enabled
            ? new UpdateScheduler(options.InitialDelay, options.RetryInterval, async token => await TickAsync(token).ConfigureAwait(false), options.Log)
            : null;
    }

    /// <summary>Once; a second call changes nothing. Starts nothing at all while the application
    /// has not switched unattended installing on.</summary>
    public void Start() => _scheduler?.Start();

    /// <summary>One pass of the decision. The scheduler runs it one at a time.</summary>
    public async Task<UnattendedTick> TickAsync(CancellationToken cancellationToken = default)
    {
        UnattendedTick tick = await DecideAsync(cancellationToken).ConfigureAwait(false);
        Report(tick);
        return tick;
    }

    /// <summary>The decision itself, ahead of the one place every path reports through.</summary>
    private async Task<UnattendedTick> DecideAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return new UnattendedTick(UnattendedOutcome.Disabled);

        // The installer is running and the application is on its way out. A tick landing in that
        // gap must not start a second one.
        if (_handedOver) return new UnattendedTick(UnattendedOutcome.InstallerStarted, "the installer is already running", _prepared?.Release);

        if (_prepared is null)
        {
            if (_release is null)
            {
                if (!CheckDue) return new UnattendedTick(UnattendedOutcome.NotDue);
                if (_options.Cadence == CheckCadence.Once) _onceChecked = true;

                UpdateFlowRun run = await _flow.RunAsync(UpdateTrigger.Silent, cancellationToken).ConfigureAwait(false);
                if (run.Result == UpdateFlowResult.CheckFailed)
                    return new UnattendedTick(UnattendedOutcome.CheckFailed, run.Check?.Detail ?? "");

                _checkedAt = _time.GetUtcNow();
                if (run.Result != UpdateFlowResult.UpdateAvailable || run.Release is null)
                    return new UnattendedTick(UnattendedOutcome.NothingToInstall);

                _release = run.Release;
            }

            PreparedUpdate prepared = await _service.PrepareAsync(_release, progress: null, cancellationToken).ConfigureAwait(false);
            if (!prepared.IsReady)
            {
                _release = null;
                return new UnattendedTick(UnattendedOutcome.NotPrepared, prepared.Detail, prepared.Release);
            }

            _prepared = prepared;
        }

        PreparedUpdate ready = _prepared;
        ReleaseInfo release = ready.Release;

        // The component's own rule first, and it is the one an application cannot reach.
        InstallMoment free = MachineFree();
        if (!free.Accepted) return Refuse(free, release);

        InstallMoment moment = _options.MayInstallNow?.Invoke(release) ?? InstallMoment.Now;
        if (!moment.Accepted) return Refuse(moment, release);

        _refusal = null;
        LaunchResult launch = _service.Launch(ready);
        if (!launch.Started)
        {
            // A verified file that would not start is downloaded and verified again by the next tick.
            _prepared = null;
            _log.Info($"The installer for {release.TagName} did not start: {launch.Detail}.");
            return new UnattendedTick(UnattendedOutcome.LaunchFailed, launch.Detail, release);
        }

        _handedOver = true;
        _log.Info($"Installer for {release.TagName} started with nobody asked; shutting down for it.");
        _options.Shutdown();
        return new UnattendedTick(UnattendedOutcome.InstallerStarted, launch.Detail, release);
    }

    /// <summary>Hands the tick to the application's own callback, whatever it decided. The only place
    /// this is called from, so a self-driven policy — the scheduler calls <see cref="TickAsync"/>
    /// internally and keeps the result to itself — reports exactly what a direct call returns.</summary>
    private void Report(UnattendedTick tick)
    {
        if (_options.TickReported is not { } reported) return;
        try
        {
            reported(tick);
        }
        catch (Exception ex)
        {
            // The callback's own fault, not the policy's: the tick it was reporting already stands.
            _log.Error(nameof(UnattendedUpdatePolicy), ex);
        }
    }

    public void Dispose() => _scheduler?.Dispose();

    private bool CheckDue =>
        _options.Cadence == CheckCadence.Once
            ? !_onceChecked
            : _checkedAt is not { } last || _time.GetUtcNow() - last >= _options.CheckInterval;

    /// <summary>A locked screen is free at once; otherwise the machine must have gone untouched for
    /// <see cref="RequiredIdle"/>.</summary>
    private InstallMoment MachineFree()
    {
        MachineIdleReading reading = _idle.Read();
        if (reading.ScreenLocked) return InstallMoment.Now;
        return reading.SinceLastInput >= RequiredIdle ? InstallMoment.Now : InstallMoment.NotNow(MachineInUse);
    }

    private UnattendedTick Refuse(InstallMoment moment, ReleaseInfo release)
    {
        // Once per reason: the same cause on every tick for an hour writes one line, and a different
        // cause writes its own.
        if (!string.Equals(_refusal, moment.Reason, StringComparison.Ordinal))
        {
            _refusal = moment.Reason;
            _log.Info($"Not installing {release.TagName} yet: {moment.Reason}.");
        }

        return new UnattendedTick(UnattendedOutcome.Refused, moment.Reason, release);
    }
}
