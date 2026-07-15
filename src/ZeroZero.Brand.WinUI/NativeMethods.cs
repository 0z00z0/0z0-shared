using System.Runtime.InteropServices;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// Minimal Win32 P/Invoke for centering <see cref="BrandAboutWindow"/> on the monitor under the
/// mouse cursor. Deliberately self-contained — this library has no dependency on any consuming
/// app's own <c>NativeMethods</c> class. Ported from the monitor-metrics subset of ChargeKeeper's
/// and HyperVManagerTray's (near-identical) <c>Helpers/NativeMethods.cs</c>.
/// Uses source-generated <see cref="LibraryImportAttribute"/> interop rather than
/// <c>DllImport</c> — no runtime marshalling stub, and it's checked for correctness at compile
/// time instead of failing at the first call.
/// </summary>
internal static partial class NativeMethods
{
    // SPI_GETWORKAREA: usable desktop area on the primary display, excluding the taskbar.
    private const uint SPI_GETWORKAREA = 0x0030;

    private const uint MONITOR_DEFAULTTONEAREST = 0x0002;
    private const int  MDT_EFFECTIVE_DPI        = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int  cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // SystemParametersInfo/GetMonitorInfo have no plain export in user32.dll, only the *A/*W
    // variants — DllImport's default ExactSpelling=false silently probed for the right suffix,
    // but LibraryImport requires an exact entry point, so the Unicode (*W) name is spelled out.
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, out RECT output, uint winIni);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [LibraryImport("Shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Work area (taskbar-excluded desktop bounds, in physical pixels) and DPI scale factor of the
    /// monitor currently under the mouse cursor — i.e. the screen whose tray the user just clicked.
    /// This positions the popup on the correct monitor and sizes it for that monitor's scaling,
    /// even in mixed-DPI multi-monitor setups. Falls back to the primary monitor at 100%.
    /// </summary>
    internal static (RECT WorkArea, double Scale) GetCursorMonitorMetrics()
    {
        if (GetCursorPos(out var cursor))
        {
            var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
            var info    = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

            if (GetMonitorInfo(monitor, ref info))
            {
                double scale = 1.0;
                if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX != 0)
                    scale = dpiX / 96.0;

                return (info.rcWork, scale);
            }
        }

        return (GetPrimaryWorkArea(), 1.0);
    }

    /// <summary>
    /// Usable desktop area on the primary monitor (total area minus the taskbar), in physical
    /// pixels. Falls back to a sensible 1080p work area if the Win32 call fails.
    /// </summary>
    private static RECT GetPrimaryWorkArea()
    {
        if (SystemParametersInfo(SPI_GETWORKAREA, 0, out var rect, 0))
            return rect;

        // Fallback: assume a typical 1920x1040 work area (1080p minus a 40px taskbar).
        return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
    }
}
