using System.Globalization;
using System.Runtime.InteropServices;
using ZeroZero.Lifecycle;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>The child process of the relaunch tests. Run with no scenario in the environment, the
/// executable does nothing, which is what the test runner expects of it. Given a scenario, it wires
/// itself against a data folder the test owns and exits the way the scenario says, so the test can
/// watch what the exit hook does from outside the process that ran it.</summary>
public static partial class Program
{
    public const string ScenarioVariable = "ZEROZERO_LIFECYCLE_SCENARIO";
    public const string DataVariable = "ZEROZERO_LIFECYCLE_DATA";
    public const string GenerationVariable = "ZEROZERO_LIFECYCLE_GENERATION";
    public const string LockVariable = "ZEROZERO_LIFECYCLE_LOCK";

    public const string UnmarkedScenario = "unmarked";
    public const string DeliberateScenario = "deliberate";
    public const string RefusedScenario = "refused";
    public const string CrashScenario = "crash";

    public const string RelaunchMarker = "relaunched.txt";
    public const string OutcomeFile = "outcome.txt";
    public const string LogFile = "log.txt";

    /// <summary>Fail critical errors, and no fault dialogue. The crash scenario would otherwise wait
    /// on a window nobody is there to close.</summary>
    private const uint QuietFaults = 0x0001 | 0x0002;

    public static int Main(string[] args)
    {
        string? scenario = Environment.GetEnvironmentVariable(ScenarioVariable);
        string? data = Environment.GetEnvironmentVariable(DataVariable);
        if (scenario is null || data is null) return 0;

        // A chain that should have stopped stops here, whatever the lifecycle decides: the tests
        // mutate the deliberate-exit mark, and an unbounded chain of relaunches is not a test result.
        int generation = int.TryParse(Environment.GetEnvironmentVariable(GenerationVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
        if (generation > 2) return 3;
        Environment.SetEnvironmentVariable(GenerationVariable, (generation + 1).ToString(CultureInfo.InvariantCulture));

        // The wiring order the guide prescribes: the lock first, and the hook only once the lock is
        // taken. Nothing is armed on the refusal path, which is the property this scenario proves.
        if (scenario == RefusedScenario)
        {
            SingleInstanceOutcome outcome = SingleInstanceLock.Acquire(Environment.GetEnvironmentVariable(LockVariable) ?? "", TimeSpan.Zero);
            File.WriteAllText(Path.Combine(data, OutcomeFile), outcome.ToString());
            return outcome.IsTaken() ? 4 : 0;
        }

        var log = new FileLogSink(Path.Combine(data, LogFile));
        var lifecycle = new ProcessLifecycle(new ProcessLifecycleOptions { DataDirectory = data, Log = log }, args);
        lifecycle.Arm();

        if (lifecycle.IsRelaunch)
        {
            File.WriteAllText(Path.Combine(data, RelaunchMarker), string.Join(' ', args));
            lifecycle.MarkDeliberateExit();
            return 0;
        }

        switch (scenario)
        {
            case UnmarkedScenario:
                return 0;
            case DeliberateScenario:
                lifecycle.MarkDeliberateExit();
                return 0;
            case CrashScenario:
                SetErrorMode(QuietFaults);
                throw new InvalidOperationException("The crash scenario, thrown on purpose with the hook armed.");
            default:
                return 2;
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetErrorMode(uint mode);
}
