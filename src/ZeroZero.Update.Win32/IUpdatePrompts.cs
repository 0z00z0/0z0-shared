namespace ZeroZero.Update.Win32;

/// <summary>What the person chose when told an update exists.</summary>
public enum InstallChoice
{
    Install,
    Later,
    OpenReleasePage,
}

/// <summary>What the flow asks and says. Native dialogs in the application; a recorder in a test,
/// where nothing appears on screen.</summary>
public interface IUpdatePrompts
{
    InstallChoice AskToInstall(ReleaseInfo release, Version runningVersion);

    void SayUpToDate(Version runningVersion);

    void SayNothingReleased();

    void SayCheckFailed(UpdateCheckResult result);

    /// <summary>The update was not prepared — refused by verification, or never downloaded — and
    /// nothing has run.</summary>
    void SayCannotInstall(PreparedUpdate update);

    void SayLaunchFailed(PreparedUpdate update, LaunchResult result);
}
