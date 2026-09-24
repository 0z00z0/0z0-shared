using Microsoft.Win32.TaskScheduler;

namespace ZeroZero.Startup;

/// <summary>The watchdog task as it should be registered, and what differs between that and a task
/// already there. The settings, the principal and the action are shared with the logon task and
/// live in <see cref="CommonTaskDefinition"/>; the three triggers are this task's own.</summary>
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

    /// <summary>No end to the repetition. The scheduler reads zero as "indefinitely", and any other
    /// value stops the probes at a moment nothing announces.</summary>
    internal static readonly TimeSpan ProbeRepetitionDuration = TimeSpan.Zero;

    internal static TaskDefinition Build(TaskService service, WatchdogTaskOptions options,
                                         TaskIdentity identity, string executablePath)
    {
        TaskDefinition definition = service.NewTask();
        definition.RegistrationInfo.Description = options.Description;

        CommonTaskDefinition.ApplyPrincipal(definition.Principal, identity);

        definition.Triggers.Add(new TimeTrigger
        {
            StartBoundary = ProbeStartBoundary,
            Repetition = { Interval = options.Interval, Duration = ProbeRepetitionDuration },
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

        definition.Actions.Add(CommonTaskDefinition.Action(executablePath, options.Arguments));

        CommonTaskDefinition.ApplyPowerSafeSettings(definition.Settings);
        // A probe missed while the machine was off runs at the next opportunity rather than being
        // dropped, which is the case the watchdog exists for.
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.Enabled = true;
        return definition;
    }

    /// <summary>Every way a registered task differs from the one <see cref="Build"/> would make, in
    /// words a log line can carry. Empty when nothing needs rewriting. Unlike the logon task, the
    /// enabled flag is a deviation: the watchdog is the application's own backstop rather than a
    /// choice the person made, so a disabled one is repaired.
    /// <para>Neither a path nor an account name appears here. The list is written at information
    /// level on an ordinary start, and the install path belongs to the failure path alone.</para>
    /// </summary>
    internal static IReadOnlyList<string> Deviations(TaskDefinition registered, WatchdogTaskOptions options,
                                                     string executablePath, TaskIdentity identity)
    {
        var found = new List<string>();
        TaskSettings settings = registered.Settings;

        if (!settings.StartWhenAvailable) found.Add("skips a probe missed while the machine was off");
        if (!settings.Enabled) found.Add("is disabled");

        // One account owns the task at a time. A task left behind by another person on a shared
        // machine probes in their session and never reaches this one's.
        if (!CommonTaskDefinition.IsIdentity(registered.Principal.UserId, identity))
            found.Add("runs as another account");

        TimeTrigger? repeating = registered.Triggers.OfType<TimeTrigger>().FirstOrDefault();
        if (repeating is null)
            found.Add("has no repeating probe");
        else
        {
            if (repeating.Repetition.Interval != options.Interval)
                found.Add($"probes every {repeating.Repetition.Interval} rather than every {options.Interval}");
            if (repeating.Repetition.Duration != ProbeRepetitionDuration)
                found.Add($"stops probing after {repeating.Repetition.Duration}");
            if (repeating.StartBoundary != ProbeStartBoundary)
                found.Add($"probes from {repeating.StartBoundary:yyyy-MM-dd HH:mm:ss} rather than from a boundary already past");
            if (!repeating.Enabled)
                found.Add("has a disabled repeating probe");
        }

        SessionStateChangeTrigger? unlock = registered.Triggers.OfType<SessionStateChangeTrigger>()
            .FirstOrDefault(t => t.StateChange == TaskSessionStateChangeType.SessionUnlock);
        if (unlock is null)
            found.Add("does not probe when the workstation is unlocked");
        else
        {
            if (unlock.Delay != options.UnlockDelay)
                found.Add($"probes {unlock.Delay} after an unlock rather than {options.UnlockDelay}");
            if (!CommonTaskDefinition.IsIdentity(unlock.UserId, identity))
                found.Add("probes on another account's unlock");
        }

        EventTrigger? resume = registered.Triggers.OfType<EventTrigger>()
            .FirstOrDefault(t => string.Equals(t.Subscription, ResumeSubscription, StringComparison.Ordinal));
        if (resume is null)
            found.Add("does not probe when the machine resumes");
        else if (resume.Delay != options.ResumeDelay)
            found.Add($"probes {resume.Delay} after a resume rather than {options.ResumeDelay}");

        CommonTaskDefinition.AddDeviations(found, registered, executablePath, options.Arguments);
        return found;
    }
}
