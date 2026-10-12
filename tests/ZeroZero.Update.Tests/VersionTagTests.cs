using Xunit;

namespace ZeroZero.Update.Tests;

public class VersionTagTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3.0")]
    [InlineData("V1.2.3", "1.2.3.0")]
    [InlineData("1.2.3", "1.2.3.0")]
    [InlineData("v1.2", "1.2.0.0")]
    [InlineData("v1.2.3.4", "1.2.3.4")]
    [InlineData(" v2.7.4 ", "2.7.4.0")]
    public void TryParse_ReadsATagAsAFourPartVersion(string tag, string expected)
    {
        Assert.True(VersionTag.TryParse(tag, out Version version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("v1")]
    [InlineData("v1.2.3-beta.1")]
    [InlineData("nightly")]
    [InlineData("mqtt-v0.7.0")]
    public void TryParse_RefusesWhatIsNotAPlainVersion(string? tag)
    {
        Assert.False(VersionTag.TryParse(tag, out _));
    }

    /// <summary>One suffix rule for a tag and the running version: a pre-release is refused on both
    /// sides, and only the commit stamp after <c>+</c> is dropped from the running version.</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("1.2.3-beta")]
    [InlineData("1.2.3-beta.1+1a2b3c4")]
    [InlineData("v1.2.3-rc1")]
    public void TryParseRunning_RefusesAPreReleaseAsATagIs(string running)
    {
        Assert.False(VersionTag.TryParse(running, out _));
        Assert.False(VersionTag.TryParseRunning(running, out _));
    }

    [Trait(Guard.Category, Guard.Value)]
    [Theory]
    [InlineData("1.2.3+1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b", "1.2.3.0")]
    [InlineData("1.2.3", "1.2.3.0")]
    public void TryParseRunning_DropsTheCommitStamp(string running, string expected)
    {
        Assert.True(VersionTag.TryParseRunning(running, out Version version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void Normalise_MakesTheRunningVersionComparableToItsOwnTag()
    {
        // Version orders 1.2.3 before 1.2.3.0, which would call the running build out of date.
        Assert.True(VersionTag.TryParse("v1.2.3", out Version tagged));
        Version running = VersionTag.Normalise(new Version(1, 2, 3));

        Assert.False(tagged > running);
        Assert.Equal(tagged, running);
    }

    [Theory]
    [InlineData("v1.35.0", "1.35.0")]
    [InlineData("2.7.4", "2.7.4")]
    public void NumberOf_IsTheTagWithoutItsPrefix(string tag, string expected)
    {
        Assert.Equal(expected, VersionTag.NumberOf(tag));
    }
}
