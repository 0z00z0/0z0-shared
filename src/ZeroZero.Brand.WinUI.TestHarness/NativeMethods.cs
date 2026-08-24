using System.Runtime.InteropServices;

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
}
