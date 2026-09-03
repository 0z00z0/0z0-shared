using System.Globalization;
using ZeroZero.Win32;

namespace ZeroZero.Update.Win32;

/// <summary>The update dialogs: worded here, marshalled by the Win32 foundation. The question is a
/// task dialog with command links where the process carries the common-controls manifest, and a
/// yes-or-no message box everywhere else; the rest are message boxes.</summary>
public sealed class NativeUpdatePrompts : IUpdatePrompts
{
    internal const int InstallId = 100;
    internal const int LaterId = 101;
    internal const int ReleasePageId = 102;

    internal const string NothingReleasedText = "No release has been published yet.";

    private readonly IntPtr _owner;
    private readonly string _applicationName;
    private readonly bool _topmost;

    /// <param name="owner">The window the dialogs are modal to, or zero for none.</param>
    /// <param name="applicationName">The caption of every dialog, and the name in their text.</param>
    /// <param name="topmost">Keep the message boxes above every other window — for a tray
    /// application with no window to bring them forward.</param>
    public NativeUpdatePrompts(IntPtr owner, string applicationName, bool topmost = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        _owner = owner;
        _applicationName = applicationName;
        _topmost = topmost;
    }

    public InstallChoice AskToInstall(ReleaseInfo release, Version runningVersion)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(runningVersion);

        if (NativeTaskDialog.IsAvailable)
            return ChoiceFor(NativeTaskDialog.Show(_owner, BuildInstallRequest(release, runningVersion)));

        return NativeMessageBox.Question(_owner, _applicationName, InstallQuestion(release, runningVersion), _topmost)
            ? InstallChoice.Install
            : InstallChoice.Later;
    }

    public void SayUpToDate(Version runningVersion) =>
        NativeMessageBox.Information(_owner, _applicationName, UpToDateText(runningVersion), _topmost);

    public void SayNothingReleased() =>
        NativeMessageBox.Information(_owner, _applicationName, NothingReleasedText, _topmost);

    public void SayCheckFailed(UpdateCheckResult result) =>
        NativeMessageBox.Warning(_owner, _applicationName, CheckFailedText(result), _topmost);

    public void SayCannotInstall(PreparedUpdate update) =>
        NativeMessageBox.Error(_owner, _applicationName, CannotInstallText(update), _topmost);

    public void SayLaunchFailed(PreparedUpdate update, LaunchResult result) =>
        NativeMessageBox.Error(_owner, _applicationName, LaunchFailedText(result), _topmost);

    internal TaskDialogRequest BuildInstallRequest(ReleaseInfo release, Version runningVersion)
    {
        string notes = ReleaseNotesText.Strip(release.Body);
        return new TaskDialogRequest
        {
            Caption = _applicationName,
            Headline = $"Version {release.VersionText} is available",
            Body = $"Version {Display(runningVersion)} is installed. The installer is downloaded and verified before it runs, and {_applicationName} closes when it starts.",
            Detail = notes.Length > 0 ? notes : null,
            Buttons =
            [
                new TaskDialogButton(InstallId, "Install now\nDownload, verify and run the installer"),
                new TaskDialogButton(LaterId, "Not now\nAsk again at the next check"),
                new TaskDialogButton(ReleasePageId, "Open the release page\nRead the notes in the browser first"),
            ],
            Icon = TaskDialogIcon.Information,
            DefaultButtonId = InstallId,
            CommandLinks = true,
            AllowCancel = true,
        };
    }

    internal static InstallChoice ChoiceFor(int buttonId) => buttonId switch
    {
        InstallId => InstallChoice.Install,
        ReleasePageId => InstallChoice.OpenReleasePage,
        _ => InstallChoice.Later,
    };

    internal string InstallQuestion(ReleaseInfo release, Version runningVersion) =>
        $"Version {release.VersionText} is available; version {Display(runningVersion)} is installed.\n\nDownload, verify and run the installer now? {_applicationName} closes when the installer starts.";

    internal static string UpToDateText(Version runningVersion) => $"Version {Display(runningVersion)} is the latest.";

    internal static string CheckFailedText(UpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            UpdateCheckOutcome.RateLimited =>
                $"The update check was refused by GitHub's rate limit. Try again after {result.RateLimitResetsAt?.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) ?? "a while"}.",
            UpdateCheckOutcome.Unreachable => $"The update service could not be reached: {result.Detail}.",
            UpdateCheckOutcome.InvalidResponse => $"The update service answered with something this version does not understand: {result.Detail}.",
            _ => $"The update check did not complete: {result.Detail}.",
        };
    }

    internal static string CannotInstallText(PreparedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update.Outcome switch
        {
            PrepareOutcome.Refused =>
                $"The downloaded installer was refused and has not been run: {update.Verification?.Detail ?? update.Detail}.\n\nThe file has been deleted. {RefusalAdvice(update.Verification)}",
            PrepareOutcome.HashNotPublished =>
                "The release publishes no SHA-256 for its installer, so a download could not be verified. Nothing was downloaded and nothing has been run.",
            PrepareOutcome.HashAmbiguous =>
                "The release publishes more than one SHA-256, so the installer's cannot be told from the rest. Nothing was downloaded and nothing has been run.",
            PrepareOutcome.InstallerAssetMissing =>
                $"The release carries no file named {update.InstallerFileName}. Nothing has been run.",
            PrepareOutcome.DownloadFailed =>
                $"The download did not complete: {update.Detail}. Nothing has been run.",
            _ => $"The update was not installed: {update.Detail}.",
        };
    }

    internal static string LaunchFailedText(LaunchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return $"The installer could not be started: {result.Detail}.";
    }

    private static string RefusalAdvice(VerificationResult? verification) => verification?.Verdict switch
    {
        VerificationVerdict.HashMismatch =>
            "The download is not the file the release published. Try again later; if it happens again, take the installer from the release page.",
        VerificationVerdict.NotSigned or VerificationVerdict.SignatureInvalid or VerificationVerdict.SignerMismatch or VerificationVerdict.CertificateNotPinned =>
            "The file is not signed by the publisher this version expects. Do not run it by hand.",
        _ => "",
    };

    private static string Display(Version version) =>
        version.Revision > 0 ? version.ToString(4) : version.Build >= 0 ? version.ToString(3) : version.ToString(2);
}
