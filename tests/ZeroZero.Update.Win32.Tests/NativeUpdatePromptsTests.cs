using Xunit;
using ZeroZero.Win32;

namespace ZeroZero.Update.Win32.Tests;

/// <summary>The dialog requests and message texts, read back rather than shown: a modal dialog
/// would block the run, so what appears on screen is looked at through the harness.</summary>
public class NativeUpdatePromptsTests
{
    private readonly NativeUpdatePrompts _prompts = new(IntPtr.Zero, "Product");

    [Fact]
    public void BuildInstallRequest_CarriesTheVersionsTheNotesAndThreeChoices()
    {
        TaskDialogRequest request = _prompts.BuildInstallRequest(FakeUpdateService.Release, new Version(1, 0, 0, 0));

        Assert.Equal("Product", request.Caption);
        Assert.Equal("Version 1.2.3 is available", request.Headline);
        Assert.Contains("Version 1.0.0 is installed", request.Body);
        Assert.Contains("Product closes", request.Body);
        Assert.Contains("verified", request.Body);
        string detail = Assert.IsType<string>(request.Detail);
        Assert.Contains("• a thing", detail);
        Assert.DoesNotContain("AD26D1A4", detail);
        Assert.DoesNotContain("**", detail);
        Assert.Equal([NativeUpdatePrompts.InstallId, NativeUpdatePrompts.LaterId, NativeUpdatePrompts.ReleasePageId], request.Buttons.Select(button => button.Id));
        Assert.DoesNotContain(TaskDialogButton.CancelId, request.Buttons.Select(button => button.Id));
        Assert.Equal(NativeUpdatePrompts.InstallId, request.DefaultButtonId);
        Assert.True(request.CommandLinks);
        Assert.True(request.AllowCancel);
        Assert.Equal(TaskDialogIcon.Information, request.Icon);
    }

    [Fact]
    public void BuildInstallRequest_LeavesTheDetailOutWhenTheNotesAreEmpty()
    {
        ReleaseInfo release = FakeUpdateService.Release with { Body = "" };

        TaskDialogRequest request = _prompts.BuildInstallRequest(release, new Version(1, 0, 0, 0));

        Assert.Null(request.Detail);
    }

    [Theory]
    [InlineData(NativeUpdatePrompts.InstallId, InstallChoice.Install)]
    [InlineData(NativeUpdatePrompts.LaterId, InstallChoice.Later)]
    [InlineData(NativeUpdatePrompts.ReleasePageId, InstallChoice.OpenReleasePage)]
    [InlineData(TaskDialogButton.CancelId, InstallChoice.Later)]
    [InlineData(999, InstallChoice.Later)]
    public void ChoiceFor_MapsEveryButtonAndTheCross(int buttonId, InstallChoice expected)
    {
        Assert.Equal(expected, NativeUpdatePrompts.ChoiceFor(buttonId));
    }

    [Fact]
    public void InstallQuestion_IsTheMessageBoxForm()
    {
        string question = _prompts.InstallQuestion(FakeUpdateService.Release, new Version(1, 0, 0, 0));

        Assert.Contains("1.2.3", question);
        Assert.Contains("1.0.0", question);
        Assert.Contains("Product closes", question);
    }

    [Fact]
    public void UpToDateText_NamesTheVersion()
    {
        Assert.Equal("Version 2.7.4 is the latest.", NativeUpdatePrompts.UpToDateText(new Version(2, 7, 4, 0)));
        Assert.Equal("Version 2.7.4.1 is the latest.", NativeUpdatePrompts.UpToDateText(new Version(2, 7, 4, 1)));
    }

    [Fact]
    public void CheckFailedText_SaysWhatStoppedTheCheck()
    {
        Version running = new(1, 0, 0, 0);

        Assert.Contains("rate limit", NativeUpdatePrompts.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.RateLimited, running, RateLimitResetsAt: DateTimeOffset.UtcNow.AddMinutes(30))));
        Assert.Contains("could not be reached: no route", NativeUpdatePrompts.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.Unreachable, running, Detail: "no route")));
        Assert.Contains("does not understand: the release tag", NativeUpdatePrompts.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.InvalidResponse, running, Detail: "the release tag 'x' is not a version")));
    }

    [Theory]
    [InlineData(PrepareOutcome.HashNotPublished, "publishes no SHA-256")]
    [InlineData(PrepareOutcome.HashAmbiguous, "more than one SHA-256")]
    [InlineData(PrepareOutcome.InstallerAssetMissing, "no file named Product-Setup-1.2.3.exe")]
    [InlineData(PrepareOutcome.DownloadFailed, "did not complete")]
    public void CannotInstallText_SaysWhyAndThatNothingRan(PrepareOutcome outcome, string reason)
    {
        string text = NativeUpdatePrompts.CannotInstallText(FakeUpdateService.NotReady(outcome));

        Assert.Contains(reason, text);
        Assert.Contains("has been run", text);
    }

    [Theory]
    [InlineData(VerificationVerdict.HashMismatch, "not the file the release published")]
    [InlineData(VerificationVerdict.NotSigned, "Do not run it by hand")]
    [InlineData(VerificationVerdict.SignatureInvalid, "Do not run it by hand")]
    [InlineData(VerificationVerdict.SignerMismatch, "Do not run it by hand")]
    [InlineData(VerificationVerdict.CertificateNotPinned, "Do not run it by hand")]
    public void CannotInstallText_ForARefusalNamesTheVerdictAndTheAdvice(VerificationVerdict verdict, string advice)
    {
        string text = NativeUpdatePrompts.CannotInstallText(FakeUpdateService.NotReady(PrepareOutcome.Refused, verdict));

        Assert.Contains("was refused and has not been run", text);
        Assert.Contains("refused for the test's reason", text);
        Assert.Contains("has been deleted", text);
        Assert.Contains(advice, text);
    }

    [Fact]
    public void LaunchFailedText_CarriesTheDetail()
    {
        Assert.Equal("The installer could not be started: refused at launch: the file changed.",
            NativeUpdatePrompts.LaunchFailedText(new LaunchResult(false, "refused at launch: the file changed")));
    }

    [Fact]
    public void Construction_NeedsAnApplicationName()
    {
        Assert.Throws<ArgumentException>(() => new NativeUpdatePrompts(IntPtr.Zero, " "));
    }
}
