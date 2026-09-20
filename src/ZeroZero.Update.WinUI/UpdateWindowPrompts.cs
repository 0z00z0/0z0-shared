using ZeroZero.Update.Win32;

namespace ZeroZero.Update.WinUI;

/// <summary>
/// What the update flow asks and says, shown in the shared update window. One window at a time and
/// one run at a time: the question, the download that follows it and the answer that follows that
/// are the same window changing what it shows, and a run that only reports something opens one
/// window and closes it again.
/// </summary>
/// <remarks>
/// Every call is made on the thread that owns the windows, which is the thread the flow was called
/// on. A call does not complete until the person has chosen or closed what it put on screen, so
/// nothing in the flow runs behind a window still in front of them.
/// </remarks>
public sealed class UpdateWindowPrompts : IUpdatePrompts
{
    private readonly UpdateWindowOptions _options;
    private UpdateWindow? _window;

    public UpdateWindowPrompts(UpdateWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApplicationName);
        _options = options;
    }

    public async Task<InstallChoice> AskToInstallAsync(ReleaseInfo release, Version runningVersion)
    {
        UpdateWindow window = Open();
        InstallChoice choice = await window.AskAsync(release, runningVersion);

        // The window stays only for an install: the download runs in it, and a refusal or a
        // failure is reported in it afterwards.
        if (choice != InstallChoice.Install) Forget(window);
        return choice;
    }

    public IProgress<DownloadProgress>? BeginDownload(ReleaseInfo release) => _window?.BeginDownload(release);

    public Task SayUpToDateAsync(Version runningVersion) =>
        SayAsync(UpdateMessages.UpToDateHeadline, UpdateMessages.UpToDateText(runningVersion), attention: false);

    public Task SayNothingReleasedAsync() =>
        SayAsync(UpdateMessages.NothingReleasedHeadline, UpdateMessages.NothingReleased(), attention: false);

    public Task SayCheckFailedAsync(UpdateCheckResult result) =>
        SayAsync(UpdateMessages.CheckFailedHeadline, UpdateMessages.CheckFailedText(result), attention: true);

    public Task SayCannotInstallAsync(PreparedUpdate update) =>
        SayAsync(UpdateMessages.CannotInstallHeadline(update), UpdateMessages.CannotInstallText(update), attention: true);

    public Task SayLaunchFailedAsync(PreparedUpdate update, LaunchResult result) =>
        SayAsync(UpdateMessages.LaunchFailedHeadline, UpdateMessages.LaunchFailedText(result), attention: true);

    public void Dismiss()
    {
        UpdateWindow? window = _window;
        _window = null;
        window?.Dismiss();
    }

    /// <summary>The window a message goes in: the one already on screen where the run is mid-flight,
    /// a new one where the run has nothing open.</summary>
    private async Task SayAsync(string headline, string body, bool attention)
    {
        UpdateWindow window = Open();
        await window.ShowMessageAsync(headline, body, attention);
        Forget(window);
    }

    private UpdateWindow Open() => _window ??= new UpdateWindow(_options);

    /// <summary>Lets go of a window that has closed, so the next run opens a fresh one rather than
    /// trying to change what a closed window shows.</summary>
    private void Forget(UpdateWindow window)
    {
        if (ReferenceEquals(_window, window)) _window = null;
        window.Dismiss();
    }
}
