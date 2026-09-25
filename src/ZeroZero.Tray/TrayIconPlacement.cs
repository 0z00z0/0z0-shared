namespace ZeroZero.Tray;

/// <summary>Where the shell draws a notification icon: beside the clock, or behind the chevron
/// that opens the overflow. The user's own choice, which this is only ever asked to change.</summary>
public enum TrayIconPlacement
{
    /// <summary>Behind the chevron, which is where an icon the shell has not been told about
    /// sits.</summary>
    Overflow,

    /// <summary>In the notification area itself, beside the clock.</summary>
    NotificationArea,
}
