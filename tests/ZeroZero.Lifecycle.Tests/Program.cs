using System.Globalization;
using ZeroZero.Lifecycle;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>The child process of the relaunch tests. Run with no scenario in the environment, the
/// executable does nothing, which is what the test runner expects of it. Given a scenario, it arms
/// the lifecycle against a data folder the test owns and exits the way the scenario says, so the
/// test can watch what the exit hook does from outside the process that ran it.</summary>
public static class Program
{
    public const string ScenarioVariable = "ZEROZERO_LIFECYCLE_SCENARIO";
    public const string DataVariable = "ZEROZERO_LIFECYCLE_DATA";
    public const string GenerationVariable = "ZEROZERO_LIFECYCLE_GENERATION";

    public const string UnmarkedScenario = "unmarked";
    public const string DeliberateScenario = "deliberate";

    public const string RelaunchMarker = "relaunched.txt";
    public const string LogFile = "log.txt";

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
            default:
                return 2;
        }
    }
}
