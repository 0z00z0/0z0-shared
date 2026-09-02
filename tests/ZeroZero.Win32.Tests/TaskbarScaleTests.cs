using System.Runtime.InteropServices;
using Xunit;

namespace ZeroZero.Win32.Tests;

public partial class TaskbarScaleTests
{
    [Fact]
    public void ScaleForTaskbar_IsTheTaskbarWindowsDpiOverNinetySix()
    {
        // The taskbar window through the test's own imports: its DPI, not the cursor monitor's
        // and not this process's.
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        Assert.NotEqual(IntPtr.Zero, taskbar);
        uint dpi = GetDpiForWindow(taskbar);
        Assert.NotEqual(0u, dpi);

        Assert.Equal(dpi / 96.0, MonitorMetrics.ScaleForTaskbar());
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? className, string? windowName);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr window);
}
