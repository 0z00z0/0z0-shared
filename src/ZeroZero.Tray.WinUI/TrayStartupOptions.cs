namespace ZeroZero.Tray.WinUI;

/// <summary>The bounds TrayStartup.StartAsync works within.</summary>
public sealed class TrayStartupOptions
{
    public TimeSpan ShellWait { get; init; } = TimeSpan.FromSeconds(15);

    public int CreateAttempts { get; init; } = 5;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ShellWait, TimeSpan.Zero, nameof(ShellWait));
        ArgumentOutOfRangeException.ThrowIfLessThan(CreateAttempts, 1, nameof(CreateAttempts));
        ArgumentOutOfRangeException.ThrowIfLessThan(RetryDelay, TimeSpan.Zero, nameof(RetryDelay));
    }
}
