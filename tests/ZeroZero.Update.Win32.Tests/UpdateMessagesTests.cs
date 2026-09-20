using Xunit;

namespace ZeroZero.Update.Win32.Tests;

/// <summary>The sentences the update puts in front of a person, read back rather than shown: what
/// they look like on screen is looked at through the harness.</summary>
public class UpdateMessagesTests
{
    [Fact]
    public void TheQuestion_CarriesBothVersionsAndWhatInstallingCosts()
    {
        string headline = UpdateMessages.AvailableHeadline(FakeUpdateService.Release);
        string body = UpdateMessages.InstallBody(new Version(1, 0, 0, 0), "Product");

        Assert.Equal("Version 1.2.3 is available", headline);
        Assert.Contains("Version 1.0.0 is installed", body);
        Assert.Contains("Product closes", body);
        Assert.Contains("verified", body);
    }

    [Fact]
    public void TheDownload_NamesTheVersionAndCountsInMegabytes()
    {
        Assert.Equal("Downloading version 1.2.3", UpdateMessages.DownloadingHeadline(FakeUpdateService.Release));
        Assert.Contains("of", UpdateMessages.DownloadProgressText(1048576, 10485760));
        Assert.Contains("so far", UpdateMessages.DownloadProgressText(1048576, null));
    }

    [Fact]
    public void UpToDateText_NamesTheVersion()
    {
        Assert.Equal("Version 2.7.4 is the latest.", UpdateMessages.UpToDateText(new Version(2, 7, 4, 0)));
        Assert.Equal("Version 2.7.4.1 is the latest.", UpdateMessages.UpToDateText(new Version(2, 7, 4, 1)));
    }

    [Fact]
    public void CheckFailedText_SaysWhatStoppedTheCheck()
    {
        Version running = new(1, 0, 0, 0);

        Assert.Contains("rate limit", UpdateMessages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.RateLimited, running, RateLimitResetsAt: DateTimeOffset.UtcNow.AddMinutes(30))));
        Assert.Contains("could not be reached: no route", UpdateMessages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.Unreachable, running, Detail: "no route")));
        Assert.Contains("does not understand: the release tag", UpdateMessages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.InvalidResponse, running, Detail: "the release tag 'x' is not a version")));
        Assert.Contains("did not answer in time: no answer", UpdateMessages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.TimedOut, running, Detail: "no answer within 10 s")));
        Assert.Contains("answered with an error: api.example", UpdateMessages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.RequestFailed, running, Detail: "api.example answered HTTP 500 rather than a release")));
    }

    /// <summary>Each outcome that stops a check gets a sentence of its own, so two situations the
    /// result now tells apart are not read back as one message.</summary>
    [Fact]
    public void CheckFailedText_SaysSomethingDifferentForEveryOutcomeThatStopsACheck()
    {
        Version running = new(1, 0, 0, 0);
        UpdateCheckOutcome[] stopped =
        [
            UpdateCheckOutcome.RateLimited, UpdateCheckOutcome.Unreachable, UpdateCheckOutcome.TimedOut,
            UpdateCheckOutcome.RequestFailed, UpdateCheckOutcome.InvalidResponse,
        ];

        string[] texts = [.. stopped.Select(outcome =>
            UpdateMessages.CheckFailedText(new UpdateCheckResult(outcome, running, Detail: "why")))];

        Assert.Equal(stopped.Length, texts.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(PrepareOutcome.HashNotPublished, "publishes no SHA-256")]
    [InlineData(PrepareOutcome.HashAmbiguous, "more than one SHA-256")]
    [InlineData(PrepareOutcome.InstallerAssetMissing, "no file named Product-Setup-1.2.3.exe")]
    [InlineData(PrepareOutcome.DownloadFailed, "did not complete")]
    public void CannotInstallText_SaysWhyAndThatNothingRan(PrepareOutcome outcome, string reason)
    {
        PreparedUpdate update = FakeUpdateService.NotReady(outcome);

        string text = UpdateMessages.CannotInstallText(update);

        Assert.Contains(reason, text);
        Assert.EndsWith(UpdateMessages.RefusalAdvice, text);
        Assert.Equal("Version 1.2.3 was not installed", UpdateMessages.CannotInstallHeadline(update));
    }

    /// <summary>Each verdict is worded for itself, every refusal ends the same way, and nothing
    /// claims the file was removed: removing it is best-effort and its failures are swallowed.</summary>
    [Theory]
    [InlineData(VerificationVerdict.HashMismatch, "not the file the release published")]
    [InlineData(VerificationVerdict.NotSigned, "carries no signature")]
    [InlineData(VerificationVerdict.SignatureInvalid, "altered since it was signed")]
    [InlineData(VerificationVerdict.SignerMismatch, "signed by another publisher")]
    [InlineData(VerificationVerdict.CertificateNotPinned, "certificate this version does not accept")]
    public void CannotInstallText_ForARefusalNamesTheVerdictAndSaysNothingRan(VerificationVerdict verdict, string sentence)
    {
        string text = UpdateMessages.CannotInstallText(FakeUpdateService.NotReady(PrepareOutcome.Refused, verdict));

        Assert.Contains(sentence, text);
        Assert.EndsWith(UpdateMessages.RefusalAdvice, text);
        Assert.DoesNotContain("delet", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchFailedText_CarriesTheDetail()
    {
        string text = UpdateMessages.LaunchFailedText(new LaunchResult(false, "refused at launch: the file changed"));

        Assert.StartsWith("The installer could not be started: refused at launch: the file changed.", text);
        Assert.EndsWith(UpdateMessages.RefusalAdvice, text);
    }
}
