using Microsoft.Win32;
using Xunit;
using ZeroZero.Diagnostics.Dumps;
using ZeroZero.Diagnostics.Tests;

namespace ZeroZero.Diagnostics.Dumps.Tests;

/// <summary>The one measurement that proves the registration does what it is for: a real
/// registration under the machine hive, a real crash in a separate process, a real dump file. It
/// needs elevation and is skipped, with the reason, without it.</summary>
public class RealDumpTests : IDisposable
{
    private const string VictimName = "ZeroZero.Diagnostics.Dumps.Tests.exe";
    private static readonly TimeSpan DumpWait = TimeSpan.FromSeconds(60);

    private readonly Scratch _scratch = new();
    private readonly RecordingSink _log = new();

    public void Dispose() => _scratch.Dispose();

    [ElevatedFact]
    public void AnUnhandledExceptionInARealProcessWritesARealDumpUnderTheMachineHive()
    {
        var registration = new DumpRegistration(Registry.LocalMachine, _log);
        var policy = new DumpPolicy(VictimName, _scratch.Directory, DumpType.Mini, 2);
        bool wasArmedBefore = registration.IsArmed(VictimName);
        Assert.False(wasArmedBefore, "a registration for the victim was already there; a previous run did not clean up");

        registration.Arm(policy);
        try
        {
            Assert.Equal(policy, registration.Read(VictimName));

            var result = ChildProcess.Run(ChildProcess.OwnExecutable("ZeroZero.Diagnostics.Dumps.Tests"), "crash");
            Assert.Equal(unchecked((int)0xE0434352), result.ExitCode);

            string expected = Path.Combine(_scratch.Directory, $"{VictimName}.{result.ProcessId}.dmp");
            var deadline = DateTime.UtcNow + DumpWait;
            while (!File.Exists(expected) && DateTime.UtcNow < deadline) Thread.Sleep(250);

            Assert.True(File.Exists(expected), $"no dump at {expected} within {DumpWait}; files present: {string.Join(", ", Directory.GetFiles(_scratch.Directory).Select(Path.GetFileName))}; victim said: {result.StandardError.Trim()}");
            Assert.True(new FileInfo(expected).Length > 0);
        }
        finally
        {
            registration.Disarm(VictimName);
        }

        Assert.False(registration.IsArmed(VictimName));
    }
}
