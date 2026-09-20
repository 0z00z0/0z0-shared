using System.Globalization;

namespace ZeroZero.Update.Win32;

/// <summary>
/// What the update says, as plain sentences. Kept apart from the window that shows them so a
/// surface written outside this repository — a console tool, a notification, an application's own
/// panel — words an outcome exactly as the shared window does.
/// </summary>
/// <remarks>
/// Every refusal ends the same way, and none of them says the file was removed: removing it is
/// best-effort, its failures are logged rather than shown, and a sentence claiming it would be
/// false in front of a person often enough to matter.
/// </remarks>
public static class UpdateMessages
{
    internal const string NothingReleasedText = "No release has been published yet.";

    /// <summary>How every refusal ends.</summary>
    public const string RefusalAdvice = "The file was not run. Update from the release page instead.";

    /// <summary>The headline over the install question.</summary>
    public static string AvailableHeadline(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return $"Version {release.VersionText} is available";
    }

    /// <summary>What installing costs and what it does, under the headline.</summary>
    public static string InstallBody(Version runningVersion, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(runningVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return $"Version {Display(runningVersion)} is installed. The installer is downloaded and verified before it runs, and {applicationName} closes when it starts.";
    }

    /// <summary>The headline while the installer is coming down.</summary>
    public static string DownloadingHeadline(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return $"Downloading version {release.VersionText}";
    }

    /// <summary>What happens when the download finishes, under the headline.</summary>
    public static string DownloadBody(string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return $"{applicationName} closes when the installer starts.";
    }

    /// <summary>The line under the progress bar: how much has arrived, and of how much where the
    /// total is known. Sizes in the current culture, because a person reads them.</summary>
    public static string DownloadProgressText(long bytesReceived, long? totalBytes) =>
        totalBytes is > 0
            ? $"{Megabytes(bytesReceived)} of {Megabytes(totalBytes.Value)} MB"
            : $"{Megabytes(bytesReceived)} MB so far";

    /// <summary>That line once the last byte has arrived. Verification reports nothing, so a bar
    /// sitting full with a byte count under it reads as a download that stalled.</summary>
    public const string VerifyingText = "Checking the hash and the signature.";

    /// <summary>The headline once the last byte has arrived and the download is being checked.</summary>
    public static string VerifyingHeadline(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return $"Checking version {release.VersionText}";
    }

    /// <summary>The headline over the notice that nothing newer exists.</summary>
    public const string UpToDateHeadline = "Up to date";

    public static string UpToDateText(Version runningVersion)
    {
        ArgumentNullException.ThrowIfNull(runningVersion);
        return $"Version {Display(runningVersion)} is the latest.";
    }

    /// <summary>The headline over the notice that the repository has published nothing.</summary>
    public const string NothingReleasedHeadline = "Nothing released yet";

    public static string NothingReleased() => NothingReleasedText;

    /// <summary>The headline over a check that did not get an answer.</summary>
    public const string CheckFailedHeadline = "The update check did not complete";

    public static string CheckFailedText(UpdateCheckResult result)
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

    /// <summary>The headline over a refusal, naming the version that did not arrive.</summary>
    public static string CannotInstallHeadline(PreparedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return $"Version {update.Release.VersionText} was not installed";
    }

    /// <summary>Why the update was not installed: one plain sentence per reason, each ending with
    /// <see cref="RefusalAdvice"/>.</summary>
    public static string CannotInstallText(PreparedUpdate update)
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
        return $"{reason}{Environment.NewLine}{Environment.NewLine}{RefusalAdvice}";
    }

    /// <summary>The headline over an installer that was verified and would not start.</summary>
    public const string LaunchFailedHeadline = "The installer did not start";

    public static string LaunchFailedText(LaunchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return $"The installer could not be started: {result.Detail}.{Environment.NewLine}{Environment.NewLine}{RefusalAdvice}";
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

    private static string Megabytes(long bytes) =>
        (bytes / 1024.0 / 1024.0).ToString("N1", CultureInfo.CurrentCulture);

    private static string Display(Version version) =>
        version.Revision > 0 ? version.ToString(4) : version.Build >= 0 ? version.ToString(3) : version.ToString(2);
}
