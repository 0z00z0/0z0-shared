using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace ZeroZero.Controls.WinUI;

/// <summary>
/// Paints a window's system title bar for a theme. Dark gets the dark set; light leaves an
/// untouched bar alone, and paints the light set on a bar that was dark before. The theme is an
/// argument, so an application pinned dark passes <see cref="ElementTheme.Dark"/> whatever the
/// system says, and one that follows the system calls <see cref="Follow"/> once and is re-painted
/// on every live change.
/// </summary>
/// <remarks>
/// A bar that has been painted once cannot be handed back to the system: its colours set to null
/// afterwards draw black, not the light bar an untouched window has (measured). Hence the two
/// rules above, and hence the light set the palette carries.
/// </remarks>
public static class TitleBarTheming
{
    /// <summary>
    /// Paints the bar for <paramref name="theme"/>. <see cref="ElementTheme.Default"/> resolves to
    /// the window content's requested theme, then its actual theme, then the application's.
    /// <paramref name="palette"/> replaces the dark set; it is never applied to a light bar. Does
    /// nothing on a Windows whose title bar cannot be recoloured.
    /// </summary>
    public static void Apply(Window window, ElementTheme theme, TitleBarPalette? palette = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        var bar = window.AppWindow.TitleBar;
        if (Resolve(theme, window) == TitleBarTheme.Dark)
        {
            Paint(bar, palette ?? TitleBarPalette.Dark);
        }
        else if (bar.ButtonBackgroundColor.HasValue)
        {
            // Painted before, so the untouched bar is gone for good: paint the light set.
            Paint(bar, TitleBarPalette.Light);
        }
    }

    /// <summary>
    /// Paints the bar for the content's theme now, and again whenever it changes, so a window
    /// open while the system theme switches keeps a bar that matches its page.
    /// </summary>
    public static void Follow(Window window, TitleBarPalette? palette = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        Apply(window, ElementTheme.Default, palette);
        if (window.Content is not FrameworkElement root) return;

        // ActualTheme is only trustworthy once the content is in a live tree: before that a root
        // with no RequestedTheme reports the application's theme. Painted again on load, and on
        // every change after.
        root.Loaded += (sender, _) => Apply(window, ((FrameworkElement)sender).ActualTheme, palette);
        root.ActualThemeChanged += (sender, _) => Apply(window, sender.ActualTheme, palette);
    }

    private static TitleBarTheme Resolve(ElementTheme theme, Window window) => theme switch
    {
        ElementTheme.Dark => TitleBarTheme.Dark,
        ElementTheme.Light => TitleBarTheme.Light,
        _ => window.Content is FrameworkElement root
            ? Resolve(root.RequestedTheme != ElementTheme.Default ? root.RequestedTheme : root.ActualTheme, window)
            : (Application.Current.RequestedTheme == ApplicationTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light),
    };

    private static void Paint(AppWindowTitleBar bar, TitleBarPalette set)
    {
        bar.BackgroundColor = ToColor(set.Background);
        bar.InactiveBackgroundColor = ToColor(set.InactiveBackground);
        bar.ForegroundColor = ToColor(set.Foreground);
        bar.InactiveForegroundColor = ToColor(set.InactiveForeground);
        bar.ButtonBackgroundColor = ToColor(set.ButtonBackground);
        bar.ButtonInactiveBackgroundColor = ToColor(set.ButtonInactiveBackground);
        bar.ButtonForegroundColor = ToColor(set.ButtonForeground);
        bar.ButtonInactiveForegroundColor = ToColor(set.ButtonInactiveForeground);
        bar.ButtonHoverBackgroundColor = ToColor(set.ButtonHoverBackground);
        bar.ButtonHoverForegroundColor = ToColor(set.ButtonHoverForeground);
        bar.ButtonPressedBackgroundColor = ToColor(set.ButtonPressedBackground);
        bar.ButtonPressedForegroundColor = ToColor(set.ButtonPressedForeground);
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
