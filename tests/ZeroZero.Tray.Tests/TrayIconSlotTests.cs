using System.Runtime.InteropServices;
using Xunit;

namespace ZeroZero.Tray.Tests;

public partial class TrayIconSlotTests
{
    [Theory]
    [InlineData(1.0, 16)]
    [InlineData(1.25, 20)]
    [InlineData(1.5, 24)]
    [InlineData(1.75, 28)]
    [InlineData(2.0, 32)]
    [InlineData(2.5, 40)]
    [InlineData(3.0, 48)]
    // 17.6 rounds to 18, not down to a slot the shell would then stretch.
    [InlineData(1.1, 18)]
    public void PixelsFor_IsSixteenAtTheScaleRounded(double scale, int pixels)
    {
        Assert.Equal(pixels, TrayIconSlot.PixelsFor(scale));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.5)]
    [InlineData(double.NaN)]
    public void PixelsFor_RefusesAScaleThatIsNotAFactor(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrayIconSlot.PixelsFor(scale));
    }

    [Fact]
    public void PixelsForTaskbar_IsTheSlotAtTheTaskbarWindowsOwnDpi()
    {
        // The taskbar's DPI read through the test's own imports, from the taskbar window rather
        // than from this process or the primary monitor.
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        Assert.NotEqual(IntPtr.Zero, taskbar);
        uint dpi = GetDpiForWindow(taskbar);
        Assert.NotEqual(0u, dpi);

        Assert.Equal(TrayIconSlot.PixelsFor(dpi / 96.0), TrayIconSlot.PixelsForTaskbar());
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? className, string? windowName);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr window);
}
