using System.Resources;
using Microsoft.Extensions.Localization;
using ZeroZero.Primitives;

namespace ZeroZero.Update.Win32;

/// <summary>What the update says, in the UI culture, the host's localizer asked first. Apart from
/// the window, so a console tool or a notification words an outcome as the window does. No refusal
/// says the file was removed: removal is best-effort. A core detail clause stays English.</summary>
public sealed class UpdateMessages(IStringLocalizer? host = null)
{
    private static readonly ResourceManager Resources = new("ZeroZero.Update.Win32.Texts", typeof(UpdateMessages).Assembly);

    private readonly LibraryStringLocalizer _text = new(Resources, host);

    /// <summary>How every refusal ends.</summary>
    public string RefusalAdvice => Text(nameof(RefusalAdvice));

    /// <summary>Each button says its act, never "OK".</summary>
    public string InstallLabel => Text(nameof(InstallLabel));

    public string LaterLabel => Text(nameof(LaterLabel));

    public string ReleasePageLabel => Text(nameof(ReleasePageLabel));

    /// <summary>Also the screen-reader name of the cross that closes the window.</summary>
    public string CloseLabel => Text(nameof(CloseLabel));

    public string CancelDownloadLabel => Text(nameof(CancelDownloadLabel));

    public string AvailableHeadline(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return Text(nameof(AvailableHeadline), release.VersionText);
    }

    public string InstallBody(Version runningVersion, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(runningVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return Text(nameof(InstallBody), Display(runningVersion), applicationName);
    }

    public string DownloadingHeadline(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return Text(nameof(DownloadingHeadline), release.VersionText);
    }

    public string DownloadBody(string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return Text(nameof(DownloadBody), applicationName);
    }

    /// <summary>Megabytes received, and of how many where the total is known.</summary>
    public string DownloadProgressText(long bytesReceived, long? totalBytes) =>
        totalBytes is > 0
            ? Text("DownloadProgress", Megabytes(bytesReceived), Megabytes(totalBytes.Value))
            : Text("DownloadProgressSoFar", Megabytes(bytesReceived));

    /// <summary>The line once the last byte has arrived: a full bar over a byte count reads as stalled.</summary>
    public string VerifyingText => Text(nameof(VerifyingText));

    public string VerifyingHeadline(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return Text(nameof(VerifyingHeadline), release.VersionText);
    }

    public string UpToDateHeadline => Text(nameof(UpToDateHeadline));

    public string UpToDateText(Version runningVersion)
    {
        ArgumentNullException.ThrowIfNull(runningVersion);
        return Text(nameof(UpToDateText), Display(runningVersion));
    }

    public string NothingReleasedHeadline => Text(nameof(NothingReleasedHeadline));

    public string NothingReleased() => Text("NothingReleasedText");

    public string CheckFailedHeadline => Text(nameof(CheckFailedHeadline));

    public string CheckFailedText(UpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            UpdateCheckOutcome.RateLimited => result.RateLimitResetsAt is { } resets
                ? Text("CheckRateLimited", resets.ToLocalTime())
                : Text("CheckRateLimitedLater"),
            UpdateCheckOutcome.Unreachable => Text("CheckUnreachable", result.Detail),
            UpdateCheckOutcome.TimedOut => Text("CheckTimedOut", result.Detail),
            UpdateCheckOutcome.RequestFailed => Text("CheckRequestFailed", result.Detail),
            UpdateCheckOutcome.InvalidResponse => Text("CheckInvalidResponse", result.Detail),
            _ => Text("CheckFailedOther", result.Detail),
        };
    }

    public string CannotInstallHeadline(PreparedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return Text(nameof(CannotInstallHeadline), update.Release.VersionText);
    }

    /// <summary>One sentence per reason, then <see cref="RefusalAdvice"/> after a blank line.</summary>
    public string CannotInstallText(PreparedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        string reason = update.Outcome switch
        {
            PrepareOutcome.Refused => RefusedText(update),
            PrepareOutcome.HashNotPublished => Text("HashNotPublished"),
            PrepareOutcome.HashAmbiguous => Text("HashAmbiguous"),
            PrepareOutcome.InstallerAssetMissing => Text("InstallerAssetMissing", update.InstallerFileName),
            PrepareOutcome.DownloadFailed => Text("DownloadFailed", update.Detail),
            _ => Text("NotInstalledOther", update.Detail),
        };
        return WithAdvice(reason);
    }

    public string LaunchFailedHeadline => Text(nameof(LaunchFailedHeadline));

    public string LaunchFailedText(LaunchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return WithAdvice(Text(nameof(LaunchFailedText), result.Detail));
    }

    public string HeldHeadline(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return Text(nameof(HeldHeadline), release.VersionText);
    }

    /// <summary>The hold's own clause, after "because" or the translation's own conjunction.</summary>
    public string HeldText(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return Text(nameof(HeldText), reason);
    }

    /// <summary>The hold reason where a hold function throws or answers blank.</summary>
    public string HoldUnreadable => Text(nameof(HoldUnreadable));

    /// <summary>One wording per language, so a machine in use all afternoon logs one line.</summary>
    public string MachineInUse => Text(nameof(MachineInUse));

    public string InstallerRunning => Text(nameof(InstallerRunning));

    // The verifier's detail is carried where it names something, left out where it only repeats.
    private string RefusedText(PreparedUpdate update) => update.Verification?.Verdict switch
    {
        VerificationVerdict.HashMismatch => Text("RefusedHashMismatch", update.Verification.Detail),
        VerificationVerdict.NotSigned => Text("RefusedNotSigned"),
        VerificationVerdict.SignatureInvalid => Text("RefusedSignatureInvalid"),
        VerificationVerdict.SignerMismatch => Text("RefusedSignerMismatch", update.Verification.Detail),
        VerificationVerdict.CertificateNotPinned => Text("RefusedCertificateNotPinned", update.Verification.Detail),
        VerificationVerdict.FileMissing => Text("RefusedFileMissing"),
        VerificationVerdict.SignatureCheckFailed => Text("RefusedSignatureCheckFailed", update.Verification.Detail),
        _ => Text("RefusedOther", update.Verification?.Detail ?? update.Detail),
    };

    private string WithAdvice(string reason) =>
        $"{reason}{Environment.NewLine}{Environment.NewLine}{RefusalAdvice}";

    private string Text(string key) => _text[key].Value;

    private string Text(string key, params object?[] arguments) => _text[key, arguments!].Value;

    private static double Megabytes(long bytes) => bytes / 1024.0 / 1024.0;

    private static string Display(Version version) =>
        version.Revision > 0 ? version.ToString(4) : version.Build >= 0 ? version.ToString(3) : version.ToString(2);
}
