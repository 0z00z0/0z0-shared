namespace ZeroZero.Update.Win32;

/// <summary>What the person chose when told an update exists.</summary>
public enum InstallChoice
{
    Install,
    Later,
    OpenReleasePage,
}

/// <summary>
/// What the flow asks and says. The shared update window in the application; a recorder in a test,
/// where nothing appears on screen.
/// </summary>
/// <remarks>
/// Every call is awaited, and a call does not complete until the person has answered or read what
/// it put on screen, so nothing runs behind a window still in front of them. An implementation that
/// draws nothing returns a completed task.
/// <para>
/// One implementation serves one run from beginning to end, so it may keep a window between calls
/// and change what it shows rather than opening another.
/// </para>
/// </remarks>
public interface IUpdatePrompts
{
    /// <summary>Asks whether to install, and does not complete until the person has chosen.</summary>
    Task<InstallChoice> AskToInstallAsync(ReleaseInfo release, Version runningVersion);

    /// <summary>
    /// The download is starting. Returns where its progress goes, or null where nothing is drawn.
    /// Called after the person chose to install and before the first byte; the flow reports to this
    /// and to the application's own reporter both.
    /// </summary>
    IProgress<DownloadProgress>? BeginDownload(ReleaseInfo release);

    Task SayUpToDateAsync(Version runningVersion);

    Task SayNothingReleasedAsync();

    Task SayCheckFailedAsync(UpdateCheckResult result);

    /// <summary>The update was not prepared — refused by verification, or never downloaded — and
    /// nothing has run.</summary>
    Task SayCannotInstallAsync(PreparedUpdate update);

    Task SayLaunchFailedAsync(PreparedUpdate update, LaunchResult result);

    /// <summary>
    /// Takes whatever is on screen off it. Called once the installer is running and the application
    /// is about to exit for it, so nothing of the update is left in front of a person watching the
    /// application close.
    /// </summary>
    void Dismiss();
}
