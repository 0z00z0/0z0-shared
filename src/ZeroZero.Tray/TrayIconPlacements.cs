namespace ZeroZero.Tray;

/// <summary>
/// The decision of what to write into the shell's per-icon settings, kept apart from the writing.
/// The settings belong to the person using the machine — one entry per icon the shell has seen,
/// each holding whether that icon sits in the notification area or in the overflow — so the rule
/// is narrow: one entry, found by the icon's own identity, written only when it does not already
/// read as wanted, and nothing at all in every other case.
/// </summary>
/// <remarks>The identity is the one the application registered with the shell, which is why an
/// application must supply a fixed one of its own. A match on the executable path would be wrong:
/// the shell rewrites the path on the entry when the binary moves, and several entries can carry
/// one path.</remarks>
public static class TrayIconPlacements
{
    /// <summary>Where the shell keeps the settings, under the current user.</summary>
    public const string SettingsKey = @"Control Panel\NotifyIconSettings";

    /// <summary>The value holding the identity of the icon an entry belongs to.</summary>
    public const string IconGuidValue = "IconGuid";

    /// <summary>The value the taskbar settings page writes: a DWORD of 1 for the notification
    /// area. Absent on every entry the user has never moved.</summary>
    public const string IsPromotedValue = "IsPromoted";

    /// <summary>The placement a raw registry value means: the notification area on a DWORD of 1,
    /// and the overflow on zero, on absence and on a value of another kind.</summary>
    public static TrayIconPlacement FromRegistryValue(object? value) =>
        value is int and 1 ? TrayIconPlacement.NotificationArea : TrayIconPlacement.Overflow;

    /// <summary>The value that means a placement, to write back.</summary>
    public static int ToRegistryValue(TrayIconPlacement placement) =>
        placement == TrayIconPlacement.NotificationArea ? 1 : 0;

    /// <summary>
    /// The one entry to write and the value to write into it, or null when nothing should be
    /// written: no entry carries this icon, the entry already reads as wanted, or more than one
    /// entry carries the identity, which is two installations this process cannot tell apart.
    /// </summary>
    public static TrayIconPlacementPlan? Plan(IEnumerable<TrayIconSetting> settings, Guid icon, TrayIconPlacement wanted)
    {
        ArgumentNullException.ThrowIfNull(settings);

        TrayIconSetting? found = null;
        foreach (var setting in settings)
        {
            if (setting.Icon != icon) continue;
            if (found is not null) return null;
            found = setting;
        }

        if (found is not { } entry || entry.Placement == wanted) return null;
        return new TrayIconPlacementPlan(entry.Entry, ToRegistryValue(wanted));
    }
}
