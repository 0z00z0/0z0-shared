using System.Runtime.InteropServices;

namespace ZeroZero.Tray.Tests;

/// <summary>The process-state read the host test makes from outside the process that created
/// the icon: whether the kernel throttles the process's execution speed. Source-generated, as the
/// assemblies under test.</summary>
internal static partial class NativeMethods
{
    private const int ProcessPowerThrottling = 4;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;

    /// <summary>The bit that says the process runs at the efficient quality-of-service level.</summary>
    internal const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    /// <summary>The throttling state of another process, read through its handle.</summary>
    internal static PROCESS_POWER_THROTTLING_STATE ReadPowerThrottling(IntPtr process)
    {
        var state = new PROCESS_POWER_THROTTLING_STATE { Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION };
        if (!GetProcessInformation(process, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
            throw new InvalidOperationException($"GetProcessInformation failed with {Marshal.GetLastPInvokeError()}.");
        return state;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessInformation(IntPtr process, int informationClass, ref PROCESS_POWER_THROTTLING_STATE state, uint size);
}
