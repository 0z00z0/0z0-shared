using System.Runtime.InteropServices;

namespace ZeroZero.Update;

/// <summary>What the machine looks like at one moment. A measurement and nothing more: what counts
/// as free enough to start an installer is decided above this.</summary>
/// <param name="SinceLastInput">How long since the last keyboard or mouse input in this session.
/// <see cref="TimeSpan.Zero"/> when the reading did not come back, so a machine that cannot be read
/// never looks untouched.</param>
/// <param name="ScreenLocked">True only where the session reported itself locked. An unknown or
/// unreadable state is false, so a failed reading never stands in for a locked screen.</param>
public readonly record struct MachineIdleReading(TimeSpan SinceLastInput, bool ScreenLocked);

/// <summary>Reads the machine. The real session in an application; a fake in a test, where no input
/// and no lock exist.</summary>
public interface IMachineIdle
{
    MachineIdleReading Read();
}

/// <summary>The session this process runs in. Stateless, so one instance serves everything.</summary>
public sealed class MachineIdle : IMachineIdle
{
    public static readonly MachineIdle Instance = new();

    public MachineIdleReading Read() => new(SinceLastInput(), ScreenLocked());

    private static TimeSpan SinceLastInput()
    {
        var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
        if (!NativeMethods.GetLastInputInfo(ref info)) return TimeSpan.Zero;

        // Both counts wrap at the same 32-bit boundary, so the unsigned difference stays right
        // across it; a signed subtraction would read as 49 days of idleness once a month.
        uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    private static bool ScreenLocked()
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!NativeMethods.WTSQuerySessionInformation(IntPtr.Zero, NativeMethods.WTS_CURRENT_SESSION, NativeMethods.WTSSessionInfoEx, out buffer, out uint bytes))
                return false;
            if (buffer == IntPtr.Zero || bytes < (uint)Marshal.SizeOf<NativeMethods.WTSINFOEX>())
                return false;

            NativeMethods.WTSINFOEX info = Marshal.PtrToStructure<NativeMethods.WTSINFOEX>(buffer);
            return info.Level == 1 && info.SessionFlags == NativeMethods.WTS_SESSIONSTATE_LOCK;
        }
        finally
        {
            if (buffer != IntPtr.Zero) NativeMethods.WTSFreeMemory(buffer);
        }
    }
}
