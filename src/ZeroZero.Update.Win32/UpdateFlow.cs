using System.ComponentModel;
using System.Diagnostics;
using ZeroZero.Primitives;

namespace ZeroZero.Update.Win32;

/// <summary>Who started the run, and how loud it is. A manual run reports every outcome; a
/// scheduled one speaks only when there is something to install, and logs the rest; a silent one
/// shows nothing at all and hands its caller the outcome to report on its own surface.</summary>
public enum UpdateTrigger
{
    Manual,
    Scheduled,

    // Appended rather than filed beside the triggers it sits between, so the two above keep the
    // numbers they already had.

    /// <summary>Nothing reaches the screen, whatever the outcome, a release included. The caller
    /// reads the run and drives its own button; an update starts from
    /// <see cref="UpdateFlow.InstallAsync"/> with the release the run carries.</summary>
    Silent,
}

public enum UpdateFlowResult
{
    /// <summary>An install is already in progress; this one did nothing. A check is never refused
    /// this way: a caller arriving while one is in flight joins it.</summary>
    AlreadyRunning,
    UpToDate,
    NothingReleased,
    CheckFailed,
    Declined,
    ReleasePageOpened,

    /// <summary>The update was refused or could not be downloaded. Nothing ran; the person was told.</summary>
    CannotInstall,
    LaunchFailed,

    /// <summary>The installer is running and the shutdown callback has been called.</summary>
    InstallerStarted,

    // Appended, so the members above keep the numbers they already had.

    /// <summary>A release newer than the running version was found and nothing was shown, because
    /// the trigger was <see cref="UpdateTrigger.Silent"/>. The run carries the release.</summary>
    UpdateAvailable,
}

/// <summary>What a run did, and what it found.</summary>
/// <param name="Check">The check this run read, or null where the run started from a release
/// already found and checked nothing.</param>
/// <param name="Release">The release newer than the running version, where there is one — what
/// <see cref="UpdateFlow.InstallAsync"/> takes to install without checking again.</param>
public sealed record UpdateFlowRun(UpdateFlowResult Result, UpdateCheckResult? Check = null, ReleaseInfo? Release = null);

public sealed class UpdateFlowOptions
{
    /// <summary>What the application does once the installer is running: mark its exit deliberate
    /// and exit. Called after the installer process exists and never before; when and how the
    /// application exits is its own decision.</summary>
    public required Action Shutdown { get; init; }

    /// <summary>Opens the release page in the browser. The shell when null.</summary>
    public Action<Uri>? OpenReleasePage { get; init; }

    /// <summary>Where the installer download's progress goes while an install runs, or null for
    /// none. The flow draws nothing of its own from it: the report reaches whichever surface the
    /// application attached, which decides what to show and marshals to its own thread. Both
    /// install paths carry it — <see cref="UpdateFlow.RunAsync"/> and
    /// <see cref="UpdateFlow.InstallAsync"/> — and a run that downloads nothing, a silent check
    /// among them, reports nothing.</summary>
    public IProgress<DownloadProgress>? Progress { get; init; }

    public ILogSink Log { get; init; } = NullLogSink.Instance;
}

/// <summary>Check, ask, download, verify, launch, hand over. Call <see cref="RunAsync"/> from the
/// thread that owns the dialogs: the continuation after each await comes back to the caller's
/// context, which is where the prompts appear.</summary>
public sealed class UpdateFlow
{
    private readonly IUpdateService _service;
    private readonly IUpdatePrompts _prompts;
    private readonly UpdateFlowOptions _options;
    private readonly ILogSink _log;
    private readonly Lock _checkGate = new();
    private Task<UpdateCheckResult>? _check;
    private int _installing;

    public UpdateFlow(IUpdateService service, IUpdatePrompts prompts, UpdateFlowOptions options)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Shutdown);
        _service = service;
        _prompts = prompts;
        _options = options;
        _log = options.Log;
    }

    public async Task<UpdateFlowRun> RunAsync(UpdateTrigger trigger, CancellationToken cancellationToken = default)
    {
        bool manual = trigger == UpdateTrigger.Manual;

        Task<UpdateCheckResult> shared = SharedCheck(cancellationToken, out bool mine);
        UpdateCheckResult check = mine ? await shared : await shared.WaitAsync(cancellationToken);

        // Only a release goes on from here. Every other outcome is named, and the default catches
        // one added later: without it a new outcome would reach the install path with no release.
        switch (check.Outcome)
        {
            case UpdateCheckOutcome.UpdateAvailable:
                break;
            case UpdateCheckOutcome.UpToDate:
                if (manual) _prompts.SayUpToDate(check.RunningVersion);
                return new UpdateFlowRun(UpdateFlowResult.UpToDate, check);
            case UpdateCheckOutcome.NoReleases:
                if (manual) _prompts.SayNothingReleased();
                return new UpdateFlowRun(UpdateFlowResult.NothingReleased, check);
            case UpdateCheckOutcome.RateLimited:
            case UpdateCheckOutcome.Unreachable:
            case UpdateCheckOutcome.TimedOut:
            case UpdateCheckOutcome.RequestFailed:
            case UpdateCheckOutcome.InvalidResponse:
            default:
                if (manual) _prompts.SayCheckFailed(check);
                return new UpdateFlowRun(UpdateFlowResult.CheckFailed, check);
        }

        ReleaseInfo release = check.Release!;
        if (trigger == UpdateTrigger.Silent)
        {
            _log.Info($"Update check: {release.TagName} is available; nothing shown, the caller reports it.");
            return new UpdateFlowRun(UpdateFlowResult.UpdateAvailable, check, release);
        }

        UpdateFlowRun installed = await GuardedInstallAsync(release, check.RunningVersion, cancellationToken);
        return installed with { Check = check };
    }

    /// <summary>Ask, download, verify, launch and hand over, for a release a check has already
    /// found. Checks nothing and reaches no network before the person has chosen.</summary>
    public Task<UpdateFlowRun> InstallAsync(ReleaseInfo release, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        return GuardedInstallAsync(release, _service.RunningVersion, cancellationToken);
    }

    /// <summary>The check every caller shares. One arriving while a check is in flight is handed
    /// that check and reads its result, rather than starting a second one or being refused. The
    /// slot is cleared as the check ends, before any caller resumes, so the next request starts a
    /// fresh check rather than reading an answer that has already been given.</summary>
    /// <param name="mine">Whether this caller started the check. The one that started it awaits it
    /// directly, so its own token is the one inside the request; a caller that joined waits under
    /// its own token instead.</param>
    private Task<UpdateCheckResult> SharedCheck(CancellationToken cancellationToken, out bool mine)
    {
        lock (_checkGate)
        {
            if (_check is { IsCompleted: false } running)
            {
                mine = false;
                return running;
            }

            Task<UpdateCheckResult> fresh = CheckAndClearAsync(cancellationToken);
            // A check that finished before returning has already cleared the slot; storing it
            // would leave a finished answer where the next caller looks for one in flight.
            _check = fresh.IsCompleted ? null : fresh;
            mine = true;
            return fresh;
        }
    }

    private async Task<UpdateCheckResult> CheckAndClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _service.CheckAsync(cancellationToken);
        }
        finally
        {
            lock (_checkGate) _check = null;
        }
    }

    private async Task<UpdateFlowRun> GuardedInstallAsync(ReleaseInfo release, Version runningVersion, CancellationToken cancellationToken)
    {
        // One install at a time: a run that finds a question or a download on screen backs off.
        if (Interlocked.CompareExchange(ref _installing, 1, 0) != 0)
            return new UpdateFlowRun(UpdateFlowResult.AlreadyRunning, Release: release);
        try
        {
            return await InstallCoreAsync(release, runningVersion, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _installing, 0);
        }
    }

    private async Task<UpdateFlowRun> InstallCoreAsync(ReleaseInfo release, Version runningVersion, CancellationToken cancellationToken)
    {
        switch (_prompts.AskToInstall(release, runningVersion))
        {
            case InstallChoice.Later:
                _log.Info($"Update to {release.TagName} declined for now.");
                return new UpdateFlowRun(UpdateFlowResult.Declined, Release: release);
            case InstallChoice.OpenReleasePage:
                if (release.HtmlUri is { } page) Open(page);
                return new UpdateFlowRun(UpdateFlowResult.ReleasePageOpened, Release: release);
        }

        PreparedUpdate prepared = await _service.PrepareAsync(release, _options.Progress, cancellationToken);
        if (!prepared.IsReady)
        {
            _prompts.SayCannotInstall(prepared);
            return new UpdateFlowRun(UpdateFlowResult.CannotInstall, Release: release);
        }

        LaunchResult launch = _service.Launch(prepared);
        if (!launch.Started)
        {
            _prompts.SayLaunchFailed(prepared, launch);
            return new UpdateFlowRun(UpdateFlowResult.LaunchFailed, Release: release);
        }

        _log.Info($"Installer for {release.TagName} started; shutting down for it.");
        _options.Shutdown();
        return new UpdateFlowRun(UpdateFlowResult.InstallerStarted, Release: release);
    }

    private void Open(Uri page)
    {
        // Only a web page, and only one the release JSON named.
        if (page.Scheme != Uri.UriSchemeHttps)
        {
            _log.Info($"Not opening the release page: {page.Scheme} is not https.");
            return;
        }

        try
        {
            if (_options.OpenReleasePage is { } open)
                open(page);
            else
                Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _log.Error(nameof(UpdateFlow), ex);
        }
    }
}
