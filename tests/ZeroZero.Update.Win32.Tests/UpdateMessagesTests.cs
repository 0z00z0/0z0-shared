using System.Globalization;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ZeroZero.Update.Win32.Tests;

/// <summary>The sentences the update puts in front of a person, read back rather than shown: what
/// they look like on screen is looked at through the harness.</summary>
public class UpdateMessagesTests
{
    private static readonly UpdateMessages Messages = new();

    private static string Translation(string file) => Path.Combine(AppContext.BaseDirectory, "Translations", file);

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void TheNorwegianFileCarriesEveryKeyWithTheSamePlaceholders() =>
        TranslationFiles.AssertSameKeysAndPlaceholders(Translation("Texts.resx"), Translation("Texts.nb.resx"));

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("Texts.resx")]
    [InlineData("Texts.nb.resx")]
    public void EveryEntryTellsTheTranslatorWhatItIs(string file) =>
        TranslationFiles.AssertEveryEntryHasAComment(Translation(file));

    [Fact]
    public void EveryTextFormatsInEveryLanguage()
    {
        // A malformed format item throws where it is rendered, which is on screen rather than in a build.
        foreach (string file in new[] { "Texts.resx", "Texts.nb.resx" })
            foreach (var (key, entry) in TranslationFiles.Read(Translation(file)))
            {
                var exception = Record.Exception(() => string.Format(CultureInfo.InvariantCulture, entry.Value, new Any(), new Any()));
                Assert.True(exception is null, $"{file} {key}: {exception?.Message}");
            }
    }

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("en-GB", "en-GB", "1.5 of 10.0 MB")]
    [InlineData("nb-NO", "nb-NO", "1,5 av 10,0 MB")]
    [InlineData("nb-NO", "en-GB", "1.5 av 10.0 MB")]
    [InlineData("en-GB", "nb-NO", "1,5 of 10,0 MB")]
    public void TheUiCulturePicksTheLanguageAndTheRegionalFormatTheNumber(string language, string format, string expected) =>
        Assert.Equal(expected, UiCulture.Run(CultureInfo.GetCultureInfo(language), CultureInfo.GetCultureInfo(format),
            () => Messages.DownloadProgressText(1_572_864, 10_485_760)));

    [Fact]
    public void ThePersonsOwnDecimalSeparatorReachesTheSizes() =>
        Assert.Equal("17,8 of 46,0 MB", UiCulture.Run(CultureInfo.GetCultureInfo("en-GB"), UiCulture.EnGbWithDecimalComma(),
            () => Messages.DownloadProgressText(18_664_652, 48_234_496)));

    [Fact]
    public void TheHostsTextOutranksTheComponentsOwn()
    {
        var host = new OneText("HoldUnreadable", "die Anwendung weiss nicht, ob sie beschäftigt ist");
        var holds = new InstallHolds { Strings = host };
        holds.Hold(() => throw new InvalidOperationException("the check broke"));

        Assert.Equal("die Anwendung weiss nicht, ob sie beschäftigt ist", new UpdateMessages(host).HoldUnreadable);
        Assert.Equal("die Anwendung weiss nicht, ob sie beschäftigt ist", holds.Ask(new RecordingLogSink()).Reason);
        Assert.Equal("Install now", new UpdateMessages(host).InstallLabel);
    }

    private sealed class Any : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "x";
    }

    private sealed class OneText(string key, string value) : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name == key ? value : "", resourceNotFound: name != key);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [this[key]];
    }

    [Fact]
    public void TheQuestion_CarriesBothVersionsAndWhatInstallingCosts()
    {
        string headline = Messages.AvailableHeadline(FakeUpdateService.Release);
        string body = Messages.InstallBody(new Version(1, 0, 0, 0), "Product");

        Assert.Equal("Version 1.2.3 is available", headline);
        Assert.Contains("Version 1.0.0 is installed", body);
        Assert.Contains("Product closes", body);
        Assert.Contains("verified", body);
    }

    [Fact]
    public void TheDownload_NamesTheVersionAndCountsInMegabytes()
    {
        Assert.Equal("Downloading version 1.2.3", Messages.DownloadingHeadline(FakeUpdateService.Release));
        Assert.Contains("of", Messages.DownloadProgressText(1048576, 10485760));
        Assert.Contains("so far", Messages.DownloadProgressText(1048576, null));
    }

    [Fact]
    public void UpToDateText_NamesTheVersion()
    {
        Assert.Equal("Version 2.7.4 is the latest.", Messages.UpToDateText(new Version(2, 7, 4, 0)));
        Assert.Equal("Version 2.7.4.1 is the latest.", Messages.UpToDateText(new Version(2, 7, 4, 1)));
    }

    [Fact]
    public void CheckFailedText_SaysWhatStoppedTheCheck()
    {
        Version running = new(1, 0, 0, 0);

        Assert.Contains("rate limit", Messages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.RateLimited, running, RateLimitResetsAt: DateTimeOffset.UtcNow.AddMinutes(30))));
        Assert.Contains("could not be reached: no route", Messages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.Unreachable, running, Detail: "no route")));
        Assert.Contains("does not understand: the release tag", Messages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.InvalidResponse, running, Detail: "the release tag 'x' is not a version")));
        Assert.Contains("did not answer in time: no answer", Messages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.TimedOut, running, Detail: "no answer within 10 s")));
        Assert.Contains("answered with an error: api.example", Messages.CheckFailedText(new UpdateCheckResult(UpdateCheckOutcome.RequestFailed, running, Detail: "api.example answered HTTP 500 rather than a release")));
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
            Messages.CheckFailedText(new UpdateCheckResult(outcome, running, Detail: "why")))];

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

        string text = Messages.CannotInstallText(update);

        Assert.Contains(reason, text);
        Assert.EndsWith(Messages.RefusalAdvice, text);
        Assert.Equal("Version 1.2.3 was not installed", Messages.CannotInstallHeadline(update));
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
        string text = Messages.CannotInstallText(FakeUpdateService.NotReady(PrepareOutcome.Refused, verdict));

        Assert.Contains(sentence, text);
        Assert.EndsWith(Messages.RefusalAdvice, text);
        Assert.DoesNotContain("delet", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchFailedText_CarriesTheDetail()
    {
        string text = Messages.LaunchFailedText(new LaunchResult(false, "refused at launch: the file changed"));

        Assert.StartsWith("The installer could not be started: refused at launch: the file changed.", text);
        Assert.EndsWith(Messages.RefusalAdvice, text);
    }
}
