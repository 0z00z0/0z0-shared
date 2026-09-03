using ZeroZero.Win32;

namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// Where the settings window opens and which rectangle it remembers, as arithmetic over plain
/// numbers so both rules can be pinned without a window. The monitor lookups are the caller's:
/// which monitor a rectangle is nearest is a platform question, and it arrives as a function.
/// </summary>
internal static class WindowPlacement
{
    /// <summary>
    /// The outer rectangle to open at. A saved rectangle is kept where it was, shrunk to the work
    /// area of the monitor nearest it if it has outgrown that, and moved inside it if it has
    /// strayed — a monitor that has gone leaves a rectangle nothing can reach. With nothing saved,
    /// or a saved rectangle with no area, the default client size at the cursor monitor's scale,
    /// plus the frame, centred on that monitor's work area, which is the screen whose tray was
    /// just clicked.
    /// </summary>
    public static NativeRect Opening(
        WindowRect? saved,
        NativeRect cursorWorkArea,
        double cursorScale,
        double defaultClientWidth,
        double defaultClientHeight,
        (int Width, int Height) frame,
        Func<NativeRect, NativeRect> workAreaNearest)
    {
        ArgumentNullException.ThrowIfNull(workAreaNearest);

        if (saved is { Width: > 0, Height: > 0 } kept)
        {
            var wanted = new NativeRect(kept.X, kept.Y, kept.X + kept.Width, kept.Y + kept.Height);
            var bounds = workAreaNearest(wanted);
            int width = Math.Min(kept.Width, bounds.Width);
            int height = Math.Min(kept.Height, bounds.Height);
            return new NativeRect(kept.X, kept.Y, kept.X + width, kept.Y + height).ClampInto(bounds);
        }

        int outerWidth = Math.Min(
            (int)Math.Round(defaultClientWidth * cursorScale) + frame.Width, cursorWorkArea.Width);
        int outerHeight = Math.Min(
            (int)Math.Round(defaultClientHeight * cursorScale) + frame.Height, cursorWorkArea.Height);
        int left = cursorWorkArea.Left + (cursorWorkArea.Width - outerWidth) / 2;
        int top = cursorWorkArea.Top + (cursorWorkArea.Height - outerHeight) / 2;
        return new NativeRect(left, top, left + outerWidth, top + outerHeight);
    }

    /// <summary>
    /// The rectangle worth remembering, or null. A maximised or minimised window's geometry is
    /// the presenter's, not the user's — restored from it, the window would fill the screen with
    /// no way back, or open off it — and a rectangle with no area restores to nothing.
    /// </summary>
    public static WindowRect? Remember(bool restored, WindowRect rect) =>
        restored && rect.Width > 0 && rect.Height > 0 ? rect : null;
}
