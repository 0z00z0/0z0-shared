using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace ZeroZero.Tray.Tests;

/// <summary>
/// The host provoked for real: the interactive harness is started as a child in its tray mode,
/// creates the icon through the host, and records what it created to a probe file the test
/// owns. The process is then measured from outside, by the test, through the child's handle:
/// its priority class and its power-throttling state. That is the defect this component exists
/// to prevent: the notify-icon library's creation call defaults to an efficiency mode that puts
/// the whole process at idle priority under power throttling and never restores it, and a test
/// that only reads the argument the host passes proves the argument, not the process.
/// </summary>
public sealed class TrayHostProcessTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZeroZero.Tray.Tests." + Guid.NewGuid().ToString("N"));

    private string Probe => Path.Combine(_dir, "tray-probe.txt");

    public TrayHostProcessTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [WindowsAppRuntimeFact]
    public void TheProcessKeepsNormalPriorityAndIsNotThrottledOnceTheIconIsCreated()
    {
        using Process child = StartHarness("--tray");
        try
        {
            var probe = AwaitProbe(child);
            Assert.Equal("True", probe["created"]);
            Assert.Equal(child.Id.ToString(CultureInfo.InvariantCulture), probe["pid"]);

            // Read from outside the process that created the icon, through the handle the test
            // holds on it, so nothing the host says about itself is taken on trust.
            child.Refresh();
            var throttling = NativeMethods.ReadPowerThrottling(child.Handle);
            // Both measurements in one report: the mode sets both, and a report naming one says
            // nothing about the other.
            Assert.Multiple(
                () => Assert.Equal(ProcessPriorityClass.Normal, child.PriorityClass),
                () => Assert.Equal(0u, throttling.StateMask & NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED));
        }
        finally
        {
            Stop(child);
        }

        Assert.Equal(0, child.ExitCode);
    }

    [WindowsAppRuntimeFact]
    public void AnIconTheApplicationWroteItselfIsCreatedByPath()
    {
        using Process child = StartHarness("--tray --file");
        try
        {
            var probe = AwaitProbe(child);
            Assert.Equal("True", probe["created"]);
            Assert.EndsWith("harness-own.ico", probe["icon"], StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(probe["icon"]), "The application's own icon file is not on disk: " + probe["icon"]);
        }
        finally
        {
            Stop(child);
        }

        Assert.Equal(0, child.ExitCode);
    }

    [WindowsAppRuntimeFact]
    public void TheIconIsRenderedAtTheTaskbarsOwnSlot()
    {
        using Process child = StartHarness("--tray");
        try
        {
            var probe = AwaitProbe(child);
            Assert.Equal(TrayIconSlot.PixelsForTaskbar().ToString(CultureInfo.InvariantCulture), probe["slot"]);
            Assert.Equal(TaskbarThemes.Read().ToString(), probe["theme"]);
            Assert.True(File.Exists(probe["icon"]), "The rendered icon file is not on disk: " + probe["icon"]);
        }
        finally
        {
            Stop(child);
        }
    }

    private Process StartHarness(string arguments)
    {
        string harness = HarnessLocator.Find();
        var start = new ProcessStartInfo(harness, $"{arguments} --probe \"{Probe}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(harness)!,
        };
        return Process.Start(start) ?? throw new InvalidOperationException("The harness did not start.");
    }

    private Dictionary<string, string> AwaitProbe(Process child)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!File.Exists(Probe))
        {
            // The exit code is readable only once the process has exited, so it is read after
            // the check and never in the message of the check itself.
            if (child.HasExited) Assert.Fail($"The harness exited with {child.ExitCode} before it wrote the probe.");
            Assert.True(DateTime.UtcNow < deadline, "The harness wrote no probe within the wait.");
            Thread.Sleep(100);
        }

        return File.ReadAllLines(Probe)
            .Select(line => line.Split('\t', 2))
            .ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : "", StringComparer.Ordinal);
    }

    private void Stop(Process child)
    {
        File.WriteAllText(Probe + ".stop", "");
        if (!child.WaitForExit((int)Patience.TotalMilliseconds)) child.Kill();
    }
}
