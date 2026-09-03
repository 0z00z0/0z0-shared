using System.Runtime.InteropServices;

namespace ZeroZero.Diagnostics.Dumps.Tests;

/// <summary>The crash victim: launched by the elevated test under a real registration, it dies of an
/// unhandled exception so Windows Error Reporting writes a real dump. The test host never enters here.</summary>
public static partial class Program
{
    [LibraryImport("kernel32.dll")]
    private static partial uint GetErrorMode();

    [LibraryImport("kernel32.dll")]
    private static partial uint SetErrorMode(uint mode);

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "crash")
        {
            // A build server's job shell runs with SEM_NOGPFAULTERRORBOX and every child inherits it;
            // under that flag the OS fault path is skipped and Windows Error Reporting never sees
            // the crash. Cleared, the victim dies as a shell-launched application does, with mode 0.
            uint inherited = SetErrorMode(0);
            Console.Error.WriteLine($"crash victim: error mode 0x{inherited:X} inherited, now 0x{GetErrorMode():X}");
            throw new InvalidOperationException("provoked crash for a dump");
        }

        Console.Error.WriteLine("crash victim: crash");
        return 2;
    }
}
