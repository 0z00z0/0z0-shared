using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;
using Xunit;
using ZeroZero.Startup;

namespace ZeroZero.Startup.Tests;

/// <summary>The definition as the real scheduler library builds it, without registering anything.
/// The identity is invented: nothing here reaches the scheduler beyond a connection to it.</summary>
public sealed class StartupTaskDefinitionTests : IDisposable
{
    private static readonly TaskIdentity Identity = new(@"MACHINE\someone", "S-1-5-21-1-2-3-1001");
    private static readonly string Executable = Path.Combine(Path.GetTempPath(), "ZeroZero.Startup.Tests", "app.exe");

    private readonly TaskService _service = new();

    public void Dispose() => _service.Dispose();

    private TaskDefinition Build(bool enabled = true, string arguments = "") =>
        StartupTaskDefinition.Build(_service, new StartupTaskOptions { TaskName = "ZeroZero.Startup.Tests.Definition", Description = "described", Arguments = arguments }, Identity, Executable, enabled);

    [Fact]
    public void ThePrincipalTakesTheSidAndTheTriggerTheAccountName()
    {
        using TaskDefinition definition = Build();

        Assert.Equal(Identity.Sid, definition.Principal.UserId);
        LogonTrigger trigger = Assert.IsType<LogonTrigger>(Assert.Single(definition.Triggers));
        Assert.Equal(Identity.AccountName, trigger.UserId);
    }

    [Fact]
    public void TheTaskRunsElevatedOnTheInteractiveToken()
    {
        using TaskDefinition definition = Build();

        Assert.Equal(TaskRunLevel.Highest, definition.Principal.RunLevel);
        Assert.Equal(TaskLogonType.InteractiveToken, definition.Principal.LogonType);
        Assert.Equal("described", definition.RegistrationInfo.Description);
    }

    [Fact]
    public void TheActionIsTheExecutableInItsOwnFolder()
    {
        using TaskDefinition definition = Build();

        ExecAction action = Assert.IsType<ExecAction>(Assert.Single(definition.Actions));
        Assert.Equal(Executable, action.Path);
        Assert.True(string.IsNullOrEmpty(action.Arguments));
        Assert.Equal(Path.GetDirectoryName(Executable), action.WorkingDirectory);
    }

    [Fact]
    public void ArgumentsReachTheAction()
    {
        using TaskDefinition definition = Build(arguments: "--startup");

        Assert.Equal("--startup", Assert.IsType<ExecAction>(Assert.Single(definition.Actions)).Arguments);
    }

    [Fact]
    public void TheSettingsArePowerSafe()
    {
        using TaskDefinition definition = Build();
        TaskSettings settings = definition.Settings;

        Assert.False(settings.DisallowStartIfOnBatteries);
        Assert.False(settings.StopIfGoingOnBatteries);
        Assert.False(settings.AllowHardTerminate);
        Assert.Equal(TimeSpan.Zero, settings.ExecutionTimeLimit);
        Assert.Equal(TaskInstancesPolicy.IgnoreNew, settings.MultipleInstances);
        Assert.False(settings.RunOnlyIfIdle);
        Assert.Equal(ProcessPriorityClass.Normal, settings.Priority);
    }

    [Fact]
    public void TheSchedulersOwnDefaultsAreNotPowerSafe()
    {
        // The reason the settings exist: a fresh definition starts only on mains power, stops on
        // battery, and is killed after three days.
        using TaskDefinition fresh = _service.NewTask();

        Assert.True(fresh.Settings.DisallowStartIfOnBatteries);
        Assert.True(fresh.Settings.StopIfGoingOnBatteries);
        Assert.NotEqual(TimeSpan.Zero, fresh.Settings.ExecutionTimeLimit);
    }

    [Fact]
    public void TheEnabledFlagIsWhatWasAsked()
    {
        using TaskDefinition enabled = Build(enabled: true);
        using TaskDefinition disabled = Build(enabled: false);

        Assert.True(enabled.Settings.Enabled);
        Assert.False(disabled.Settings.Enabled);
    }

    [Fact]
    public void ATaskAsBuiltHasNoDeviations()
    {
        using TaskDefinition definition = Build(arguments: "--startup");

        Assert.Empty(StartupTaskDefinition.Deviations(definition, Executable, "--startup"));
    }

    [Fact]
    public void TheEnabledFlagIsNotADeviation()
    {
        using TaskDefinition definition = Build(enabled: false);

        Assert.Empty(StartupTaskDefinition.Deviations(definition, Executable, ""));
    }

    [Fact]
    public void TheExecutablePathIsComparedWithoutCase()
    {
        using TaskDefinition definition = Build();

        Assert.Empty(StartupTaskDefinition.Deviations(definition, Executable.ToUpperInvariant(), ""));
    }

    [Theory]
    [InlineData("battery start", "mains power")]
    [InlineData("battery stop", "goes on battery")]
    [InlineData("hard terminate", "hard-terminated")]
    [InlineData("time limit", "execution time limit")]
    [InlineData("instances", "multiple-instance")]
    [InlineData("idle", "only when idle")]
    [InlineData("priority", "priority")]
    [InlineData("run level", "does not run elevated")]
    [InlineData("logon type", "logon type")]
    [InlineData("no trigger", "no logon trigger")]
    [InlineData("no action", "starts nothing")]
    [InlineData("other executable", "rather than")]
    [InlineData("other arguments", "passes")]
    public void EachSettingAnOlderBuildLeftBehindIsNamedAsADeviation(string tamper, string expected)
    {
        using TaskDefinition definition = Build();
        switch (tamper)
        {
            case "battery start": definition.Settings.DisallowStartIfOnBatteries = true; break;
            case "battery stop": definition.Settings.StopIfGoingOnBatteries = true; break;
            case "hard terminate": definition.Settings.AllowHardTerminate = true; break;
            case "time limit": definition.Settings.ExecutionTimeLimit = TimeSpan.FromHours(72); break;
            case "instances": definition.Settings.MultipleInstances = TaskInstancesPolicy.Parallel; break;
            case "idle": definition.Settings.RunOnlyIfIdle = true; break;
            case "priority": definition.Settings.Priority = ProcessPriorityClass.BelowNormal; break;
            case "run level": definition.Principal.RunLevel = TaskRunLevel.LUA; break;
            case "logon type": definition.Principal.LogonType = TaskLogonType.S4U; break;
            case "no trigger": definition.Triggers.Clear(); break;
            case "no action": definition.Actions.Clear(); break;
            case "other executable": ((ExecAction)definition.Actions[0]).Path = Path.Combine(Path.GetTempPath(), "old", "app.exe"); break;
            case "other arguments": ((ExecAction)definition.Actions[0]).Arguments = "--old"; break;
            default: throw new ArgumentOutOfRangeException(nameof(tamper));
        }

        IReadOnlyList<string> deviations = StartupTaskDefinition.Deviations(definition, Executable, "");

        string deviation = Assert.Single(deviations);
        Assert.Contains(expected, deviation, StringComparison.Ordinal);
    }
}
