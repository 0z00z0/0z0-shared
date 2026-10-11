using System.Runtime.InteropServices;

namespace ZeroZero.Tray.WinUI;

internal static partial class NativeMethods
{
    /// <summary>The user's double-click time, in milliseconds.</summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindWindow(string? className, string? windowName);
}
