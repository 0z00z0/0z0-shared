using Microsoft.Win32.TaskScheduler;

namespace ZeroZero.Startup;

/// <summary>The watchdog task as it should be registered, and what differs between that and a task
/// already there.</summary>
internal static class WatchdogTaskDefinition
{
    /// <summary>Resume from standby. The Power-Troubleshooter provider writes event 1 once the
    /// resume has completed, which is the first moment the application can be started again.</summary>
    internal const string ResumeSubscription =
        "<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">"
        + "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]"
        + "</Select></Query></QueryList>";

    /// <summary>Fixed and in the past. The repetition is what schedules the probes, so the boundary
    /// only has to be a start the scheduler already considers reached.</summary>
    internal static readonly DateTime ProbeStartBoundary = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    internal static TaskDefinition Build(TaskService service, WatchdogTaskOptions options,
                                         TaskIdentity identity, string executablePath)
    {
        TaskDefinition definition = service.NewTask();
        definition.RegistrationInfo.Description = options.Description;

        definition.Principal.UserId = identity.Sid;
        definition.Principal.LogonType = TaskLogonType.InteractiveToken;
        definition.Principal.RunLevel = TaskRunLevel.Highest;

        definition.Triggers.Add(new TimeTrigger
        {
            StartBoundary = ProbeStartBoundary,
            Repetition = { Interval = options.Interval },
        });
        definition.Triggers.Add(new SessionStateChangeTrigger
        {
            StateChange = TaskSessionStateChangeType.SessionUnlock,
            UserId = identity.AccountName,
            Delay = options.UnlockDelay,
        });
        definition.Triggers.Add(new EventTrigger
        {
            Subscription = ResumeSubscription,
            Delay = options.ResumeDelay,
        });

        definition.Actions.Add(new ExecAction(executablePath,
                                              options.Arguments.Length == 0 ? null : options.Arguments,
                                              Path.GetDirectoryName(executablePath)));

        StartupTaskDefinition.ApplyPowerSafeSettings(definition.Settings);
        // A probe missed while the machine was off runs at the next opportunity rather than being
        // dropped, which is the case the watchdog exists for.
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.Enabled = true;
        return definition;
    }

    /// <summary>Every way a registered task differs from the one <see cref="Build"/> would make, in
    /// words a log line can carry. Empty when nothing needs rewriting. Unlike the logon task, the
    /// enabled flag is a deviation: the watchdog is the application's own backstop rather than a
    /// choice the person made, so a disabled one is repaired.</summary>
    internal static IReadOnlyList<string> Deviations(TaskDefinition registered, WatchdogTaskOptions options,
                                                     string executablePath)
    {
        var found = new List<string>();
        TaskSettings settings = registered.Settings;

        if (settings.DisallowStartIfOnBatteries) found.Add("starts only on mains power");
        if (settings.StopIfGoingOnBatteries) found.Add("stops when the machine goes on battery");
        if (settings.AllowHardTerminate) found.Add("may be hard-terminated");
        if (settings.ExecutionTimeLimit != TimeSpan.Zero) found.Add($"has an execution time limit of {settings.ExecutionTimeLimit}");
        if (settings.MultipleInstances != TaskInstancesPolicy.IgnoreNew) found.Add($"has multiple-instance policy {settings.MultipleInstances}");
        if (settings.RunOnlyIfIdle) found.Add("runs only when idle");
        if (!settings.StartWhenAvailable) found.Add("skips a probe missed while the machine was off");
        if (!settings.Enabled) found.Add("is disabled");

        if (registered.Principal.RunLevel != TaskRunLevel.Highest) found.Add("does not run elevated");
        if (registered.Principal.LogonType != TaskLogonType.InteractiveToken) found.Add($"runs with logon type {registered.Principal.LogonType}");

        TimeTrigger? repeating = registered.Triggers.OfType<TimeTrigger>().FirstOrDefault();
        if (repeating is null)
            found.Add("has no repeating probe");
        else if (repeating.Repetition.Interval != options.Interval)
            found.Add($"probes every {repeating.Repetition.Interval} rather than every {options.Interval}");

        if (!registered.Triggers.OfType<SessionStateChangeTrigger>()
                       .Any(t => t.StateChange == TaskSessionStateChangeType.SessionUnlock))
            found.Add("does not probe when the workstation is unlocked");

        if (!registered.Triggers.OfType<EventTrigger>()
                       .Any(t => string.Equals(t.Subscription, ResumeSubscription, StringComparison.Ordinal)))
            found.Add("does not probe when the machine resumes");

        ExecAction? action = registered.Actions.OfType<ExecAction>().FirstOrDefault();
        if (action is null)
            found.Add("starts nothing");
        else
        {
            if (!string.Equals(action.Path?.Trim('"'), executablePath, StringComparison.OrdinalIgnoreCase))
                found.Add($"starts '{action.Path}' rather than '{executablePath}'");
            if (!string.Equals(action.Arguments ?? "", options.Arguments, StringComparison.Ordinal))
                found.Add($"passes '{action.Arguments}' rather than '{options.Arguments}'");
        }

        return found;
    }
}
