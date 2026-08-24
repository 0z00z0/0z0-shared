using System.Runtime.InteropServices;
using Windows.Graphics;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Minimal Win32 P/Invoke for the harness's own DPI scaling. The library's
/// <c>ZeroZero.Brand.WinUI.NativeMethods</c> is internal to that assembly, so a host carries its
/// own — exactly as a real consuming app (e.g. M365Migrator) has to.
/// Uses source-generated <see cref="LibraryImportAttribute"/> interop rather than
/// <c>DllImport</c> — no runtime marshalling stub, and it's checked for correctness at compile
/// time instead of failing at the first call.
/// </summary>
internal static partial class NativeMethods
{
    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr window, out Rect rect);

    /// <summary>
    /// DPI scale factor of the display a window is on — 1.0 at 100%, 1.75 at 175%. Converts
    /// device-independent layout measurements into the physical pixels the <c>AppWindow</c> sizing
    /// APIs take. Falls back to 1.0 if the window has no DPI yet.
    /// </summary>
    internal static double GetScaleForWindow(IntPtr window)
    {
        uint dpi = GetDpiForWindow(window);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>
    /// Physical pixels the title bar and frame borders add around a window's client area, read from
    /// the window itself. Both rectangles are already in the window's own DPI, so the difference
    /// needs no scaling. Zero if either rectangle is unavailable.
    /// </summary>
    internal static SizeInt32 GetChromeSizeForWindow(IntPtr window)
    {
        // GetClientRect always reports its origin at 0,0, so the client extent is Right/Bottom.
        if (!GetWindowRect(window, out Rect frame) || !GetClientRect(window, out Rect client))
        {
            return new SizeInt32(0, 0);
        }

        return new SizeInt32(
            frame.Right - frame.Left - client.Right,
            frame.Bottom - frame.Top - client.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }
}
