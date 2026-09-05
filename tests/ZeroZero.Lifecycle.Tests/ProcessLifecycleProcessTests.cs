using System.Diagnostics;
using System.Globalization;
using Xunit;
using ZeroZero.Lifecycle;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>The exit hook provoked for real: the test executable is started as a child with a
/// scenario in its environment, arms the lifecycle against a folder the test owns, and exits. What
/// the hook then does — start the executable again with the relaunch argument, or not — is read
/// from that folder, from outside the process that decided.</summary>
public sealed class ProcessLifecycleProcessTests : IDisposable
{
    private static readonly string Executable = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(3);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZeroZero.Lifecycle.Tests." + Guid.NewGuid().ToString("N"));

    private string Marker => Path.Combine(_dir, Program.RelaunchMarker);
    private string OutcomeFile => Path.Combine(_dir, Program.OutcomeFile);
    private string LimiterFile => Path.Combine(_dir, RelaunchLimiter.FileName);
    private string Log => File.Exists(Path.Combine(_dir, Program.LogFile)) ? File.ReadAllText(Path.Combine(_dir, Program.LogFile)) : "";

    public ProcessLifecycleProcessTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // The grandchild may still be exiting; give it a moment before the folder goes.
        WaitFor(() => Log.Contains("Exit was deliberate", StringComparison.Ordinal) || !Log.Contains("relaunched as process", StringComparison.Ordinal), Patience);
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task AnExitNobodyAskedForStartsTheExecutableAgainWithTheRelaunchArgument()
    {
        Assert.Equal(0, await RunChild(Program.UnmarkedScenario));

        Assert.True(WaitFor(() => File.Exists(Marker), Patience), "The executable was not relaunched: " + Log);
        Assert.Equal(Relaunch.Argument, File.ReadAllText(Marker).Trim());
        Assert.Contains("relaunched as process", Log, StringComparison.Ordinal);
        Assert.Single(File.ReadAllLines(LimiterFile));
        // The relaunched process exited deliberately, so the chain ended with it.
        Assert.True(WaitFor(() => Log.Contains("Exit was deliberate", StringComparison.Ordinal), Patience), Log);
    }

    [Fact]
    public async Task ADeliberateExitStartsNothing()
    {
        Assert.Equal(0, await RunChild(Program.DeliberateScenario));

        Assert.Contains("Exit was deliberate", Log, StringComparison.Ordinal);
        await Task.Delay(Silence);
        Assert.False(File.Exists(Marker), "The executable was relaunched after a deliberate exit: " + Log);
        Assert.False(File.Exists(LimiterFile));
    }

    [Fact]
    public async Task AnExhaustedBudgetStartsNothing()
    {
        string stamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        File.WriteAllLines(LimiterFile, Enumerable.Repeat(stamp, RelaunchLimiter.Limit));

        Assert.Equal(0, await RunChild(Program.UnmarkedScenario));

        Assert.Contains("Relaunch refused", Log, StringComparison.Ordinal);
        await Task.Delay(Silence);
        Assert.False(File.Exists(Marker), "The executable was relaunched past the budget: " + Log);
    }

    [Fact]
    public async Task AnInstanceRefusedTheLockArmsNothingAndIsNotRelaunched()
    {
        // The child wires itself the way the guide prescribes: the lock first, the hook only once the
        // lock is taken. Nothing is armed on the refusal path, so nothing has to be marked either.
        string name = @"Local\ZeroZero.Lifecycle.Tests." + Guid.NewGuid().ToString("N");
        using var other = new MutexHolder(name);
        Assert.True(other.Acquired);

        Assert.Equal(0, await RunChild(Program.RefusedScenario, name));

        Assert.Equal(nameof(SingleInstanceOutcome.RefusedHeld), File.ReadAllText(OutcomeFile).Trim());
        await Task.Delay(Silence);
        Assert.False(File.Exists(Marker), "The refused instance was relaunched, so its exit reached a hook.");
        Assert.False(File.Exists(LimiterFile), "The refused instance reached the limiter, so its exit reached a hook.");
    }

    [Fact]
    public async Task ACrashNeverReachesTheHook()
    {
        // The hook is armed and the process then dies of an unhandled exception. Measured rather than
        // assumed: this is what makes the arm-after-the-lock order cost nothing for a crash, since a
        // crash was never relaunched under the arm-first order either.
        Assert.NotEqual(0, await RunChild(Program.CrashScenario));

        await Task.Delay(Silence);
        Assert.False(File.Exists(Marker), "A crash was relaunched: " + Log);
        Assert.False(File.Exists(LimiterFile), "A crash reached the limiter: " + Log);
    }

    private async Task<int> RunChild(string scenario, string? lockName = null)
    {
        Assert.True(File.Exists(Executable), "The test executable is not beside the test assembly: " + Executable);

        var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true };
        start.Environment[Program.ScenarioVariable] = scenario;
        start.Environment[Program.DataVariable] = _dir;
        start.Environment.Remove(Program.GenerationVariable);
        if (lockName is not null) start.Environment[Program.LockVariable] = lockName;

        using Process child = Process.Start(start) ?? throw new InvalidOperationException("The child did not start.");
        await child.WaitForExitAsync().WaitAsync(Patience);
        return child.ExitCode;
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan patience)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < patience)
        {
            if (condition()) return true;
            Thread.Sleep(100);
        }
        return condition();
    }
}
