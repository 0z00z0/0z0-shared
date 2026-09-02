using System.Runtime.InteropServices;

namespace ZeroZero.Win32.Tests;

/// <summary>
/// A real top-level window with a title bar and borders that is never shown, so the metrics under
/// test read a frame the operating system laid out rather than one the test assumed.
/// </summary>
internal sealed partial class FramedWindow : IDisposable
{
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);

    public IntPtr Handle { get; }

    public FramedWindow()
    {
        // The pre-registered static control class needs no window procedure of its own.
        Handle = CreateWindowEx(0, "STATIC", "framed", WS_OVERLAPPEDWINDOW,
                                CW_USEDEFAULT, CW_USEDEFAULT, 400, 300,
                                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
            throw new InvalidOperationException($"The test window could not be created (error {Marshal.GetLastPInvokeError()}).");
    }

    public void Dispose() => DestroyWindow(Handle);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
                                                 int x, int y, int width, int height,
                                                 IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetDesktopWindow();

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(IntPtr window);
}
