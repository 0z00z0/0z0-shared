using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;

namespace ZeroZero.Startup;

/// <summary>What the logon task and the watchdog task define identically — the power-safe settings,
/// the principal and the action — and the deviations that follow from them. Each task keeps its own
/// triggers and its own reading of the enabled flag; everything else is here, because a check added
/// to one copy of a duplicated list is missed by the other.</summary>
internal static class CommonTaskDefinition
{
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

    /// <summary>Both tasks run as the given identity, elevated, on the interactive token: the
    /// application's manifest requires administrator, and a standard token would fail the start.</summary>
    internal static void ApplyPrincipal(TaskPrincipal principal, TaskIdentity identity)
    {
        principal.UserId = identity.Sid;
        principal.LogonType = TaskLogonType.InteractiveToken;
        principal.RunLevel = TaskRunLevel.Highest;
    }

    /// <summary>The executable in its own folder. An empty argument string is written as no
    /// arguments at all, because an empty one round-trips through the scheduler as null and the
    /// deviation read below compares against the string it was given.</summary>
    internal static ExecAction Action(string executablePath, string arguments) =>
        new(executablePath,
            arguments.Length == 0 ? null : arguments,
            Path.GetDirectoryName(executablePath));

    /// <summary>Whether a value the scheduler read back names the given identity. Three forms count,
    /// because the scheduler answers in whichever it chose to store: the security identifier, the
    /// account name with its domain, and the account name without it. Measured: a principal written
    /// by security identifier comes back as the bare account name, so a check on one form alone
    /// rewrites the task on every start.</summary>
    internal static bool IsIdentity(string? value, TaskIdentity identity)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (string.Equals(value, identity.Sid, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(value, identity.AccountName, StringComparison.OrdinalIgnoreCase)) return true;

        int separator = identity.AccountName.LastIndexOf('\\');
        return separator >= 0
            && string.Equals(value, identity.AccountName[(separator + 1)..], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every way a registered task differs from what both builders write, added to the
    /// caller's list in words a log line can carry. No path and no account name: a drift line is
    /// written at information level on an ordinary start, and the install path belongs to the
    /// failure path alone.</summary>
    internal static void AddDeviations(List<string> found, TaskDefinition registered,
                                       string executablePath, string arguments)
    {
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

        ExecAction? action = registered.Actions.OfType<ExecAction>().FirstOrDefault();
        if (action is null)
            found.Add("starts nothing");
        else
        {
            if (!string.Equals(action.Path?.Trim('"'), executablePath, StringComparison.OrdinalIgnoreCase))
                found.Add("starts another executable");
            if (!string.Equals(action.Arguments ?? "", arguments, StringComparison.Ordinal))
                found.Add("passes other arguments");
        }
    }
}
