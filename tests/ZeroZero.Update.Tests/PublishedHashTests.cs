using Xunit;

namespace ZeroZero.Update.Tests;

public class PublishedHashTests
{
    private const string Hash = "AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084";

    [Fact]
    public void FromBody_FindsTheHashInTheReleaseNotesLine()
    {
        // The shape both applications' release workflows write today.
        string body = "## Product v1.35.0\n\n### New\n- a thing\n\nDownload `Product-Setup-1.35.0.exe` below.\n\n**SHA256 (installer):** `ad26d1a44e4d772cedb730988e645fd127f7c0300678f9bd1c09c411443fe084`\n";

        PublishedHash hash = PublishedHash.FromBody(body);

        Assert.Equal(PublishedHashOutcome.Found, hash.Outcome);
        Assert.Equal(Hash, hash.Sha256Hex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Notes with no hash at all.")]
    [InlineData("A commit, not a hash: 5957b9b2c3d4e5f60718293a4b5c6d7e8f901234")]
    [InlineData("Sixty-five hex characters are not a hash: AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE0841")]
    public void FromBody_ReportsNothingPublished(string? body)
    {
        PublishedHash hash = PublishedHash.FromBody(body);

        Assert.Equal(PublishedHashOutcome.NotPublished, hash.Outcome);
        Assert.Null(hash.Sha256Hex);
    }

    [Fact]
    public void FromBody_ReportsTwoDifferentHashesAsAmbiguous()
    {
        string body = $"installer {Hash}\nportable D94238189AF72B6C84B58BF515ABF560DE1AEBF541504DC1C3034005D2D0F8FE";

        PublishedHash hash = PublishedHash.FromBody(body);

        Assert.Equal(PublishedHashOutcome.Ambiguous, hash.Outcome);
        Assert.Null(hash.Sha256Hex);
    }

    [Fact]
    public void FromBody_CountsTheSameHashTwiceAsOne()
    {
        string body = $"SHA256: {Hash}\n\nAgain, in lower case: {Hash.ToLowerInvariant()}";

        PublishedHash hash = PublishedHash.FromBody(body);

        Assert.Equal(PublishedHashOutcome.Found, hash.Outcome);
        Assert.Equal(Hash, hash.Sha256Hex);
    }

    [Fact]
    public void IsHashLine_TellsTheHashLineFromTheRest()
    {
        Assert.True(PublishedHash.IsHashLine($"**SHA256 (installer):** `{Hash}`"));
        Assert.False(PublishedHash.IsHashLine("- keep-awake: choose the screen hold from the dashboard"));
    }
}
