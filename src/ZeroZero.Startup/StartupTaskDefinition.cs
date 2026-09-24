using Microsoft.Win32.TaskScheduler;

namespace ZeroZero.Startup;

/// <summary>The task as it should be registered, and what differs between that and a task already
/// there. The settings, the principal and the action are shared with the watchdog task and live in
/// <see cref="CommonTaskDefinition"/>; the logon trigger is this task's own.</summary>
internal static class StartupTaskDefinition
{
    internal static TaskDefinition Build(TaskService service, StartupTaskOptions options, TaskIdentity identity, string executablePath, bool enabled)
    {
        TaskDefinition definition = service.NewTask();
        definition.RegistrationInfo.Description = options.Description;

        CommonTaskDefinition.ApplyPrincipal(definition.Principal, identity);

        definition.Triggers.Add(new LogonTrigger { UserId = identity.AccountName });
        definition.Actions.Add(CommonTaskDefinition.Action(executablePath, options.Arguments));

        CommonTaskDefinition.ApplyPowerSafeSettings(definition.Settings);
        definition.Settings.Enabled = enabled;
        return definition;
    }

    /// <summary>Every way a registered task differs from the one <see cref="Build"/> would make,
    /// in words a log can carry. Empty when nothing needs rewriting. The enabled flag is not a
    /// deviation: whether the application runs at logon is the user's choice.</summary>
    internal static IReadOnlyList<string> Deviations(TaskDefinition registered, string executablePath, string arguments)
    {
        var found = new List<string>();

        if (!registered.Triggers.OfType<LogonTrigger>().Any()) found.Add("has no logon trigger");

        CommonTaskDefinition.AddDeviations(found, registered, executablePath, arguments);
        return found;
    }
}
