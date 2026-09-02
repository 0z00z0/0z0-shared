using System.Runtime.InteropServices;

namespace ZeroZero.Win32;

/// <summary>
/// Where a window may go and how large a device-independent unit is there, as plain numbers in
/// physical pixels. Which monitor gets which window, and the arithmetic that places it, stay with
/// the caller.
/// </summary>
public static class MonitorMetrics
{
    /// <summary>The DPI a monitor reports at 100% scaling.</summary>
    private const double BaselineDpi = 96.0;

    /// <summary>
    /// Work area and scale of the monitor under the mouse cursor — the screen whose tray was just
    /// clicked. The primary monitor at 100% when the cursor or its monitor cannot be read.
    /// </summary>
    public static (NativeRect WorkArea, double Scale) ForCursor()
    {
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            IntPtr monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };

            if (NativeMethods.GetMonitorInfo(monitor, ref info))
                return (info.rcWork.ToNativeRect(), ScaleForMonitor(monitor));
        }

        return (PrimaryWorkArea(), 1.0);
    }

    /// <summary>
    /// The primary monitor's desktop less the taskbar. A failed call yields a 1080p desktop with a
    /// 40-pixel taskbar rather than an empty rectangle a caller would centre against.
    /// </summary>
    public static NativeRect PrimaryWorkArea()
    {
        if (NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, out var rect, 0))
            return rect.ToNativeRect();

        return new NativeRect(0, 0, 1920, 1040);
    }

    /// <summary>
    /// Scale of the monitor a window is on: 1.0 at 100%, 1.75 at 175%. Converts device-independent
    /// layout measurements into the physical pixels native sizing takes. 1.0 for a handle that is
    /// not a window, or one whose DPI is not yet known.
    /// </summary>
    public static double ScaleForWindow(IntPtr window)
    {
        uint dpi = NativeMethods.GetDpiForWindow(window);
        return dpi == 0 ? 1.0 : dpi / BaselineDpi;
    }

    /// <summary>
    /// Physical pixels the frame adds around a window's client area, read from the window itself so
    /// the answer holds at any scaling and theme. Both rectangles are in the window's own DPI, so
    /// the difference needs no scaling. Zero when either cannot be read.
    /// </summary>
    public static (int Width, int Height) NonClientSize(IntPtr window)
    {
        // GetClientRect reports its origin at 0,0, so the client extent is Right and Bottom.
        if (!NativeMethods.GetWindowRect(window, out var frame) || !NativeMethods.GetClientRect(window, out var client))
            return (0, 0);

        return (frame.Right - frame.Left - client.Right, frame.Bottom - frame.Top - client.Bottom);
    }

    private static double ScaleForMonitor(IntPtr monitor)
    {
        // S_OK and a non-zero DPI; anything else is 100%, never a zero a caller divides by.
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX != 0)
            return dpiX / BaselineDpi;

        return 1.0;
    }
}
