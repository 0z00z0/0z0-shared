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

    /// <summary>How every refusal ends. Nothing here says the file was removed: removing it is
    /// best-effort, its failures are logged rather than shown, and a sentence claiming it would be
    /// false in front of a person often enough to matter.</summary>
    internal const string RefusalAdvice = "The file was not run. Update from the release page instead.";

    private readonly IntPtr _owner;
    private readonly string _applicationName;
    private readonly bool _topmost;
    private readonly Func<ReleaseInfo, string?>? _releaseNotes;

    /// <param name="owner">The window the dialogs are modal to, or zero for none.</param>
    /// <param name="applicationName">The caption of every dialog, and the name in their text.</param>
    /// <param name="topmost">Keep the message boxes above every other window — for a tray
    /// application with no window to bring them forward.</param>
    /// <param name="releaseNotes">The text behind the install dialog's release-notes expander, for
    /// an application that keeps its own notes. Null, or a null return, takes the release body with
    /// its markdown stripped, as it is without this; an empty return leaves the expander out.</param>
    public NativeUpdatePrompts(IntPtr owner, string applicationName, bool topmost = false, Func<ReleaseInfo, string?>? releaseNotes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        _owner = owner;
        _applicationName = applicationName;
        _topmost = topmost;
        _releaseNotes = releaseNotes;
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
        // The host's text is taken as written; only the release body is stripped of its markdown.
        string notes = _releaseNotes?.Invoke(release) ?? ReleaseNotesText.Strip(release.Body);
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
            UpdateCheckOutcome.TimedOut => $"The update service did not answer in time: {result.Detail}.",
            UpdateCheckOutcome.RequestFailed => $"The update service answered with an error: {result.Detail}.",
            UpdateCheckOutcome.InvalidResponse => $"The update service answered with something this version does not understand: {result.Detail}.",
            _ => $"The update check did not complete: {result.Detail}.",
        };
    }

    /// <summary>Why the update was not installed: one plain sentence per reason, each ending the
    /// same way — the file was not run, and the release page is where to go instead.</summary>
    internal static string CannotInstallText(PreparedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        string reason = update.Outcome switch
        {
            PrepareOutcome.Refused => RefusedText(update),
            PrepareOutcome.HashNotPublished =>
                "The release publishes no SHA-256 for its installer, so a download could not be checked against one, and nothing was downloaded.",
            PrepareOutcome.HashAmbiguous =>
                "The release publishes more than one SHA-256, so the installer's cannot be told from the rest, and nothing was downloaded.",
            PrepareOutcome.InstallerAssetMissing =>
                $"The release carries no file named {update.InstallerFileName}, so there was nothing to download.",
            PrepareOutcome.DownloadFailed =>
                $"The download did not complete: {update.Detail}.",
            _ => $"The update was not installed: {update.Detail}.",
        };
        return $"{reason}\n\n{RefusalAdvice}";
    }

    /// <summary>A refusal by verification, worded per verdict. The verifier's own sentence is
    /// carried where it names something — a hash, a publisher, a thumbprint, a Windows code — and
    /// left out where it would only repeat the sentence above it.</summary>
    private static string RefusedText(PreparedUpdate update) => update.Verification?.Verdict switch
    {
        VerificationVerdict.HashMismatch =>
            $"The downloaded installer is not the file the release published: {update.Verification.Detail}.",
        VerificationVerdict.NotSigned =>
            "The downloaded installer carries no signature, so there is nothing to say who made it.",
        VerificationVerdict.SignatureInvalid =>
            "The downloaded installer has been altered since it was signed.",
        VerificationVerdict.SignerMismatch =>
            $"The downloaded installer is signed by another publisher: {update.Verification.Detail}.",
        VerificationVerdict.CertificateNotPinned =>
            $"The downloaded installer spells the expected publisher's name with a certificate this version does not accept: {update.Verification.Detail}.",
        VerificationVerdict.FileMissing =>
            "The downloaded installer was no longer there to be checked.",
        VerificationVerdict.SignatureCheckFailed =>
            $"Windows refused the downloaded installer's signature: {update.Verification.Detail}.",
        _ => $"The downloaded installer was refused: {update.Verification?.Detail ?? update.Detail}.",
    };

    internal static string LaunchFailedText(LaunchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return $"The installer could not be started: {result.Detail}.\n\n{RefusalAdvice}";
    }

    private static string Display(Version version) =>
        version.Revision > 0 ? version.ToString(4) : version.Build >= 0 ? version.ToString(3) : version.ToString(2);
}
