using System.Text.RegularExpressions;
using Xunit;

namespace ZeroZero.Brand.Core.Tests;

/// <summary>The studio-wide constants every consuming app renders verbatim. They are a published
/// contract, so a silent edit here changes what ships in several apps' About surfaces at once.</summary>
public class BrandTests
{
    [Fact]
    public void StudioName_HasNoSpaceBetweenTheZeros()
        => Assert.Equal("ZeroZero Software", Brand.StudioName);

    [Fact]
    public void StudioName_ContainsExactlyOneSpace()
        => Assert.Equal(1, Brand.StudioName.Count(c => c == ' '));

    [Fact]
    public void Tagline_IsTheStudioTagline()
        => Assert.Equal("Small tools. Zero bloat.", Brand.Tagline);

    [Theory]
    [InlineData(Brand.WebsiteUrl, "0z0.xyz")]
    [InlineData(Brand.BuyMeACoffeeUrl, "buymeacoffee.com")]
    [InlineData(Brand.OrgUrl, "github.com")]
    public void Links_AreAbsoluteHttpsUrisOnTheExpectedHost(string url, string host)
    {
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri), $"'{url}' is not an absolute URI.");
        Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
        Assert.Equal(host, uri.Host);
    }

    [Fact]
    public void OrgUrl_PointsAtTheStudioOrganisation()
        => Assert.Equal("https://github.com/0z00z0", Brand.OrgUrl);

    [Fact]
    public void WebsiteUrl_HasNoTrailingSlash()
        => Assert.False(Brand.WebsiteUrl.EndsWith('/'));

    /// <summary>The palette is consumed as raw hex by both WinUI (via a colour parser) and by
    /// documentation, so the literal form matters as much as the value.</summary>
    [Theory]
    [InlineData(Brand.ColorBg)]
    [InlineData(Brand.ColorBg2)]
    [InlineData(Brand.ColorTeal)]
    [InlineData(Brand.ColorBlue)]
    [InlineData(Brand.ColorPurple)]
    [InlineData(Brand.ColorIndigo)]
    [InlineData(Brand.ColorAmber)]
    public void Palette_IsSixDigitLowercaseHexWithAHash(string colour)
        => Assert.Matches(new Regex("^#[0-9a-f]{6}$"), colour);

    [Fact]
    public void Palette_TealIsTheAccentFromTheSiteMark()
        => Assert.Equal("#27e0c8", Brand.ColorTeal);

    [Fact]
    public void Palette_TheTwoBackgroundsDiffer()
        => Assert.NotEqual(Brand.ColorBg, Brand.ColorBg2);

    [Fact]
    public void Palette_EveryColourIsDistinct()
    {
        string[] palette =
        [
            Brand.ColorBg, Brand.ColorBg2, Brand.ColorTeal, Brand.ColorBlue,
            Brand.ColorPurple, Brand.ColorIndigo, Brand.ColorAmber,
        ];

        Assert.Equal(palette.Length, palette.Distinct(StringComparer.Ordinal).Count());
    }
}
