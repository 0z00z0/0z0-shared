namespace ZeroZero.Tray.WinUI;

/// <summary>How TrayStartup.StartAsync ended; docs/zerozero-tray.md says what each leaves behind.</summary>
public enum TrayStartupOutcome
{
    Created,
    ShellNeverAppeared,
    CreationFailed,
}

public readonly record struct TrayStartupResult(
    TrayStartupOutcome Outcome, int Attempts, TimeSpan Elapsed, Exception? LastError)
{
    public bool IsCreated => Outcome == TrayStartupOutcome.Created;
}
