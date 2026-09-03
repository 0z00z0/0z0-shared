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
