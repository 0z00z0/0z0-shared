using System.Runtime.InteropServices;

namespace ZeroZero.Win32.Tests;

/// <summary>
/// An independent reading of the monitor under the cursor — its own imports, its own structures —
/// so a test can say the assembly under test reports that monitor's work area and scale rather
/// than merely something non-empty.
/// </summary>
internal static partial class CursorMonitor
{
    private const uint MONITOR_DEFAULTTONEAREST = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    public static (NativeRect WorkArea, double Scale) Read()
    {
        if (!GetCursorPos(out Point cursor)) throw new InvalidOperationException("The cursor position is unavailable.");
        IntPtr monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("The monitor is unreadable.");
        if (GetDpiForMonitor(monitor, 0, out uint dpi, out _) != 0) throw new InvalidOperationException("The monitor DPI is unreadable.");

        return (new NativeRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom), dpi / 96.0);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("Shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
