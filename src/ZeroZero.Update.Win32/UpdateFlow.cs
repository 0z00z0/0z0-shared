using System.ComponentModel;
using System.Diagnostics;
using ZeroZero.Primitives;

namespace ZeroZero.Update.Win32;

/// <summary>Who started the run. A manual run reports every outcome; a scheduled one speaks only
/// when there is something to install, and logs the rest.</summary>
public enum UpdateTrigger
{
    Manual,
    Scheduled,
}

public enum UpdateFlowResult
{
    /// <summary>Another run is in progress; this one did nothing.</summary>
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
}

public sealed class UpdateFlowOptions
{
    /// <summary>What the application does once the installer is running: mark its exit deliberate
    /// and exit. Called after the installer process exists and never before; when and how the
    /// application exits is its own decision.</summary>
    public required Action Shutdown { get; init; }

    /// <summary>Opens the release page in the browser. The shell when null.</summary>
    public Action<Uri>? OpenReleasePage { get; init; }

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
    private int _running;

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

    public async Task<UpdateFlowResult> RunAsync(UpdateTrigger trigger, CancellationToken cancellationToken = default)
    {
        // One run at a time: a scheduled check that finds a manual one on screen backs off.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return UpdateFlowResult.AlreadyRunning;
        try
        {
            return await RunCoreAsync(trigger, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<UpdateFlowResult> RunCoreAsync(UpdateTrigger trigger, CancellationToken cancellationToken)
    {
        bool manual = trigger == UpdateTrigger.Manual;

        UpdateCheckResult check = await _service.CheckAsync(cancellationToken);
        switch (check.Outcome)
        {
            case UpdateCheckOutcome.UpToDate:
                if (manual) _prompts.SayUpToDate(check.RunningVersion);
                return UpdateFlowResult.UpToDate;
            case UpdateCheckOutcome.NoReleases:
                if (manual) _prompts.SayNothingReleased();
                return UpdateFlowResult.NothingReleased;
            case UpdateCheckOutcome.RateLimited:
            case UpdateCheckOutcome.Unreachable:
            case UpdateCheckOutcome.InvalidResponse:
                if (manual) _prompts.SayCheckFailed(check);
                return UpdateFlowResult.CheckFailed;
        }

        ReleaseInfo release = check.Release!;
        switch (_prompts.AskToInstall(release, check.RunningVersion))
        {
            case InstallChoice.Later:
                _log.Info($"Update to {release.TagName} declined for now.");
                return UpdateFlowResult.Declined;
            case InstallChoice.OpenReleasePage:
                if (release.HtmlUri is { } page) Open(page);
                return UpdateFlowResult.ReleasePageOpened;
        }

        PreparedUpdate prepared = await _service.PrepareAsync(release, _options.Progress, cancellationToken);
        if (!prepared.IsReady)
        {
            _prompts.SayCannotInstall(prepared);
            return UpdateFlowResult.CannotInstall;
        }

        LaunchResult launch = _service.Launch(prepared);
        if (!launch.Started)
        {
            _prompts.SayLaunchFailed(prepared, launch);
            return UpdateFlowResult.LaunchFailed;
        }

        _log.Info($"Installer for {release.TagName} started; shutting down for it.");
        _options.Shutdown();
        return UpdateFlowResult.InstallerStarted;
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
