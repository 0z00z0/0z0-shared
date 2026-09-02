using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;

namespace ZeroZero.Startup;

/// <summary>The task as it should be registered, and what differs between that and a task already
/// there.</summary>
internal static class StartupTaskDefinition
{
    internal static TaskDefinition Build(TaskService service, StartupTaskOptions options, TaskIdentity identity, string executablePath, bool enabled)
    {
        TaskDefinition definition = service.NewTask();
        definition.RegistrationInfo.Description = options.Description;

        definition.Principal.UserId = identity.Sid;
        definition.Principal.LogonType = TaskLogonType.InteractiveToken;
        definition.Principal.RunLevel = TaskRunLevel.Highest;

        definition.Triggers.Add(new LogonTrigger { UserId = identity.AccountName });
        definition.Actions.Add(new ExecAction(executablePath,
                                              options.Arguments.Length == 0 ? null : options.Arguments,
                                              Path.GetDirectoryName(executablePath)));

        ApplyPowerSafeSettings(definition.Settings);
        definition.Settings.Enabled = enabled;
        return definition;
    }

    /// <summary>The scheduler's defaults are for a maintenance job, not a resident application: a
    /// machine booting on battery never got the application while the scheduler reported success,
    /// and the silent execution limit killed it after three days.</summary>
    internal static void ApplyPowerSafeSettings(TaskSettings settings)
    {
        settings.DisallowStartIfOnBatteries = false;
        settings.StopIfGoingOnBatteries = false;
        settings.AllowHardTerminate = false;
        settings.ExecutionTimeLimit = TimeSpan.Zero;
        settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        settings.RunOnlyIfIdle = false;
        settings.Priority = ProcessPriorityClass.Normal;
    }

    /// <summary>Every way a registered task differs from the one <see cref="Build"/> would make,
    /// in words a log can carry. Empty when nothing needs rewriting. The enabled flag is not a
    /// deviation: whether the application runs at logon is the user's choice.</summary>
    internal static IReadOnlyList<string> Deviations(TaskDefinition registered, string executablePath, string arguments)
    {
        var found = new List<string>();
        TaskSettings settings = registered.Settings;

        if (settings.DisallowStartIfOnBatteries) found.Add("starts only on mains power");
        if (settings.StopIfGoingOnBatteries) found.Add("stops when the machine goes on battery");
        if (settings.AllowHardTerminate) found.Add("may be hard-terminated");
        if (settings.ExecutionTimeLimit != TimeSpan.Zero) found.Add($"has an execution time limit of {settings.ExecutionTimeLimit}");
        if (settings.MultipleInstances != TaskInstancesPolicy.IgnoreNew) found.Add($"has multiple-instance policy {settings.MultipleInstances}");
        if (settings.RunOnlyIfIdle) found.Add("runs only when idle");
        if (settings.Priority != ProcessPriorityClass.Normal) found.Add($"runs at {settings.Priority} priority");

        if (registered.Principal.RunLevel != TaskRunLevel.Highest) found.Add("does not run elevated");
        if (registered.Principal.LogonType != TaskLogonType.InteractiveToken) found.Add($"runs with logon type {registered.Principal.LogonType}");

        if (!registered.Triggers.OfType<LogonTrigger>().Any()) found.Add("has no logon trigger");

        ExecAction? action = registered.Actions.OfType<ExecAction>().FirstOrDefault();
        if (action is null)
            found.Add("starts nothing");
        else
        {
            if (!string.Equals(action.Path?.Trim('"'), executablePath, StringComparison.OrdinalIgnoreCase))
                found.Add($"starts '{action.Path}' rather than '{executablePath}'");
            if (!string.Equals(action.Arguments ?? "", arguments, StringComparison.Ordinal))
                found.Add($"passes '{action.Arguments}' rather than '{arguments}'");
        }

        return found;
    }
}
