using System.Runtime.InteropServices;

namespace ZeroZero.Tray.WinUI;

internal static partial class NativeMethods
{
    /// <summary>The user's double-click time, in milliseconds.</summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetDoubleClickTime();

    /// <summary>Which theme the process's own Win32 menus are drawn in: 0 leaves it to Windows,
    /// 1 allows dark where a window asks for it, 2 forces dark and 3 forces light.</summary>
    internal enum PreferredAppMode
    {
        Default = 0,
        AllowDark = 1,
        ForceDark = 2,
        ForceLight = 3,
    }

    /// <summary>Sets the theme the process's Win32 menus are drawn in, and returns what it was.
    /// Exported by ordinal only, which is the only way to reach it; absent before Windows 10
    /// 1903.</summary>
    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    internal static partial PreferredAppMode SetPreferredAppMode(PreferredAppMode mode);

    /// <summary>Throws away the menu theme data cached for the process, so the next menu opens in
    /// the mode just set rather than the one cached when the first one opened.</summary>
    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    internal static partial void FlushMenuThemes();
}
