namespace ZeroZero.Controls.WinUI;

/// <summary>
/// The twelve colours a system title bar takes, as plain ARGB values. Framework-free on purpose:
/// the numbers are what a test pins, and the WinUI side only converts them.
/// </summary>
/// <remarks>
/// <para>Two sets ship, the platform's own dark and light captions. Mica does not paint the
/// caption area, so a dark window that leaves the bar alone shows a light strip behind its
/// caption buttons; the dark set paints the strip the colour of the window it sits on.</para>
/// <para>The light set exists for one reason: a bar that has been painted once cannot be handed
/// back to the system. Setting its colours to null afterwards leaves a black bar, measured, not
/// the light one an untouched window has. So a window that has never been painted is left alone
/// on light, and one that has been painted dark is painted light again on the way back.</para>
/// <para>An application whose identity wants other values passes its own palette; the shape is
/// the same twelve properties the title bar exposes, in the same names.</para>
/// </remarks>
public sealed record TitleBarPalette(
    uint Background,
    uint InactiveBackground,
    uint Foreground,
    uint InactiveForeground,
    uint ButtonBackground,
    uint ButtonInactiveBackground,
    uint ButtonForeground,
    uint ButtonInactiveForeground,
    uint ButtonHoverBackground,
    uint ButtonHoverForeground,
    uint ButtonPressedBackground,
    uint ButtonPressedForeground)
{
    /// <summary>
    /// The platform's dark caption: the dark window ground behind the bar and its buttons, white
    /// glyphs, disabled-grey glyphs on an inactive window, and the two subtle fills a dark theme
    /// uses for hover and press, composited over the ground so the bar never depends on blending.
    /// </summary>
    public static TitleBarPalette Dark { get; } = new(
        Background: 0xFF202020,
        InactiveBackground: 0xFF202020,
        Foreground: 0xFFFFFFFF,
        InactiveForeground: 0xFF7A7A7A,
        ButtonBackground: 0xFF202020,
        ButtonInactiveBackground: 0xFF202020,
        ButtonForeground: 0xFFFFFFFF,
        ButtonInactiveForeground: 0xFF7A7A7A,
        ButtonHoverBackground: 0xFF2D2D2D,
        ButtonHoverForeground: 0xFFFFFFFF,
        ButtonPressedBackground: 0xFF292929,
        ButtonPressedForeground: 0xFFCECECE);

    /// <summary>
    /// The platform's light caption, as an untouched bar draws it: the light window ground, near-
    /// black glyphs, grey glyphs on an inactive window, and the light theme's subtle fills for
    /// hover and press over the ground.
    /// </summary>
    public static TitleBarPalette Light { get; } = new(
        Background: 0xFFF3F3F3,
        InactiveBackground: 0xFFF3F3F3,
        Foreground: 0xFF1A1A1A,
        InactiveForeground: 0xFF9C9C9C,
        ButtonBackground: 0xFFF3F3F3,
        ButtonInactiveBackground: 0xFFF3F3F3,
        ButtonForeground: 0xFF1A1A1A,
        ButtonInactiveForeground: 0xFF9C9C9C,
        ButtonHoverBackground: 0xFFEAEAEA,
        ButtonHoverForeground: 0xFF1A1A1A,
        ButtonPressedBackground: 0xFFEDEDED,
        ButtonPressedForeground: 0xFF5D5D5D);

    /// <summary>The set for a theme.</summary>
    public static TitleBarPalette For(TitleBarTheme theme) =>
        theme == TitleBarTheme.Dark ? Dark : Light;

    /// <summary>Every value in the order the title bar names them, for a caller that walks them.</summary>
    public IEnumerable<(string Name, uint Argb)> Values()
    {
        yield return (nameof(Background), Background);
        yield return (nameof(InactiveBackground), InactiveBackground);
        yield return (nameof(Foreground), Foreground);
        yield return (nameof(InactiveForeground), InactiveForeground);
        yield return (nameof(ButtonBackground), ButtonBackground);
        yield return (nameof(ButtonInactiveBackground), ButtonInactiveBackground);
        yield return (nameof(ButtonForeground), ButtonForeground);
        yield return (nameof(ButtonInactiveForeground), ButtonInactiveForeground);
        yield return (nameof(ButtonHoverBackground), ButtonHoverBackground);
        yield return (nameof(ButtonHoverForeground), ButtonHoverForeground);
        yield return (nameof(ButtonPressedBackground), ButtonPressedBackground);
        yield return (nameof(ButtonPressedForeground), ButtonPressedForeground);
    }
}
