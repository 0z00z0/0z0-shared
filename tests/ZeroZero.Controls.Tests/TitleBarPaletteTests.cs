using System.Reflection;
using Xunit;
using ZeroZero.Controls.WinUI;

namespace ZeroZero.Controls.Tests;

public class TitleBarPaletteTests
{
    private const uint DarkGround = 0xFF202020;
    private const uint LightGround = 0xFFF3F3F3;

    [Fact]
    public void For_Dark_IsTheDarkSet()
    {
        Assert.Same(TitleBarPalette.Dark, TitleBarPalette.For(TitleBarTheme.Dark));
    }

    [Fact]
    public void For_Light_IsTheLightSet()
    {
        Assert.Same(TitleBarPalette.Light, TitleBarPalette.For(TitleBarTheme.Light));
    }

    [Fact]
    public void TheTwoSetsShareNoValue()
    {
        // A value copied from one set into the other is the kind of slip a test on one set alone
        // cannot see.
        var dark = TitleBarPalette.Dark.Values().ToArray();
        var light = TitleBarPalette.Light.Values().ToArray();
        for (int i = 0; i < dark.Length; i++)
            Assert.True(dark[i].Argb != light[i].Argb, $"{dark[i].Name} is #{dark[i].Argb:X8} in both sets.");
    }

    [Theory]
    [InlineData(TitleBarTheme.Dark)]
    [InlineData(TitleBarTheme.Light)]
    public void EveryValueIsOpaque(TitleBarTheme theme)
    {
        // The bar never depends on blending: Mica paints nothing behind the caption, so a
        // translucent value would show whatever the window manager left there.
        foreach (var (name, argb) in TitleBarPalette.For(theme).Values())
            Assert.True(argb >> 24 == 0xFF, $"{theme} {name} is #{argb:X8}, not opaque.");
    }

    [Theory]
    [InlineData(TitleBarTheme.Dark, DarkGround)]
    [InlineData(TitleBarTheme.Light, LightGround)]
    public void TheFourGroundsAreTheWindowColour(TitleBarTheme theme, uint ground)
    {
        var set = TitleBarPalette.For(theme);
        Assert.Equal(ground, set.Background);
        Assert.Equal(ground, set.InactiveBackground);
        Assert.Equal(ground, set.ButtonBackground);
        Assert.Equal(ground, set.ButtonInactiveBackground);
    }

    [Fact]
    public void Dark_GlyphsAreWhiteAndDimOnAnInactiveWindow()
    {
        var dark = TitleBarPalette.Dark;
        Assert.Equal(0xFFFFFFFFu, dark.Foreground);
        Assert.Equal(0xFFFFFFFFu, dark.ButtonForeground);
        Assert.Equal(0xFFFFFFFFu, dark.ButtonHoverForeground);
        Assert.Equal(dark.InactiveForeground, dark.ButtonInactiveForeground);
        Assert.True(Grey(dark.InactiveForeground) < Grey(dark.Foreground), "inactive glyphs are not dimmer");
        Assert.True(Grey(dark.ButtonPressedForeground) < Grey(dark.ButtonForeground), "pressed glyphs are not dimmer");
    }

    [Fact]
    public void Light_GlyphsAreNearBlackAndDimOnAnInactiveWindow()
    {
        var light = TitleBarPalette.Light;
        Assert.Equal(0xFF1A1A1Au, light.Foreground);
        Assert.Equal(0xFF1A1A1Au, light.ButtonForeground);
        Assert.Equal(0xFF1A1A1Au, light.ButtonHoverForeground);
        Assert.Equal(light.InactiveForeground, light.ButtonInactiveForeground);
        Assert.True(Grey(light.InactiveForeground) > Grey(light.Foreground), "inactive glyphs are not lighter");
        Assert.True(Grey(light.ButtonPressedForeground) > Grey(light.ButtonForeground), "pressed glyphs are not lighter");
    }

    [Fact]
    public void Dark_HoverLiftsTheGroundAndPressSitsBetween()
    {
        var dark = TitleBarPalette.Dark;
        Assert.True(Grey(dark.ButtonHoverBackground) > Grey(dark.ButtonPressedBackground), "hover is not above pressed");
        Assert.True(Grey(dark.ButtonPressedBackground) > Grey(dark.ButtonBackground), "pressed is not above rest");
    }

    [Fact]
    public void Light_HoverLowersTheGroundAndPressSitsBetween()
    {
        var light = TitleBarPalette.Light;
        Assert.True(Grey(light.ButtonHoverBackground) < Grey(light.ButtonPressedBackground), "hover is not below pressed");
        Assert.True(Grey(light.ButtonPressedBackground) < Grey(light.ButtonBackground), "pressed is not below rest");
    }

    [Fact]
    public void Values_NamesEveryPropertyOnceInDeclarationOrder()
    {
        var properties = typeof(TitleBarPalette).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(uint))
            .ToArray();
        var values = TitleBarPalette.Dark.Values().ToArray();

        Assert.Equal(12, properties.Length);
        Assert.Equal(properties.Select(p => p.Name), values.Select(v => v.Name));
        Assert.Equal(properties.Select(p => (uint)p.GetValue(TitleBarPalette.Dark)!), values.Select(v => v.Argb));
    }

    // The three channels are equal in every value of both sets, so one is the shade.
    private static byte Grey(uint argb) => (byte)argb;
}
