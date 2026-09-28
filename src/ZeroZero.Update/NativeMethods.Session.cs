using System.Runtime.InteropServices;

namespace ZeroZero.Update;

/// <summary>The imports the idle reading takes: the last keyboard or mouse input in this session,
/// and the session's own lock state.</summary>
internal static partial class NativeMethods
{
    internal const uint WTS_CURRENT_SESSION = 0xFFFFFFFF;
    internal const int WTSSessionInfoEx = 25;
    internal const int WTS_SESSIONSTATE_LOCK = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    /// <summary>Only the fields the lock state needs. The union carrying them holds 64-bit times, so
    /// it is eight-byte aligned and starts after the level rather than beside it.</summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct WTSINFOEX
    {
        [FieldOffset(0)] public uint Level;
        [FieldOffset(8)] public uint SessionId;
        [FieldOffset(12)] public int SessionState;
        [FieldOffset(16)] public int SessionFlags;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLastInputInfo(ref LASTINPUTINFO info);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytes);

    [LibraryImport("wtsapi32.dll")]
    internal static partial void WTSFreeMemory(IntPtr memory);
}
