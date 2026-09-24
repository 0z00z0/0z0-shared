namespace ZeroZero.Startup;

/// <summary>What started this copy of the application. The application knows, because the probe
/// passes the relaunch argument the watchdog options already carry, and the component needs it to
/// tell a restart loop from ordinary use.</summary>
public enum WatchdogStartCause
{
    /// <summary>A person: the logon task, a shortcut, the installer's own launch. Such a start
    /// clears the restart count, because someone is at the machine and has asked for the
    /// application.</summary>
    Person,

    /// <summary>The watchdog's own probe, which found the process gone and started it again.</summary>
    Probe,
}
