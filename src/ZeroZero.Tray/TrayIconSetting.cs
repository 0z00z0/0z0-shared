namespace ZeroZero.Tray;

/// <summary>
/// One entry of the shell's per-icon settings, as read: the name of the entry it was read from,
/// the identity of the icon it belongs to, and where that icon is drawn.
/// </summary>
/// <param name="Entry">The entry's own name under the settings key. Opaque, and the only thing a
/// write needs to find it again.</param>
/// <param name="Icon">The identity of the icon the entry belongs to, which is the identity the
/// application registered.</param>
/// <param name="Placement">Where the shell draws the icon, as the entry's value says.</param>
public readonly record struct TrayIconSetting(string Entry, Guid Icon, TrayIconPlacement Placement);
