using System.Runtime.InteropServices;

namespace ZeroZero.Win32;

internal static partial class NativeMethods
{
    // Neither entry point is documented or named in the export table; both are reached by ordinal
    // and exist from Windows 10 build 18362. On an older build the call throws
    // EntryPointNotFoundException, which the caller reads as "native chrome stays light".
    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    internal static partial int SetPreferredAppMode(int mode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    internal static partial void FlushMenuThemes();
}
