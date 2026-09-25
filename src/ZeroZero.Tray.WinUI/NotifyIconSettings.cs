using Microsoft.Win32;

namespace ZeroZero.Tray.WinUI;

/// <summary>
/// The shell's per-icon settings, read and written. The shell keeps one entry per icon it has ever
/// seen, created the first time the icon appears and named by a number of its own; the entry
/// carries the identity of the icon it belongs to, and whether the icon is drawn in the
/// notification area or behind the overflow chevron.
/// </summary>
/// <remarks>
/// <para>The entry is found by the icon's identity, never by the executable's path. What to write
/// is <see cref="TrayIconPlacements.Plan"/>'s decision; this only carries it out, and writes
/// nothing while the shell has no entry for the icon, which is the case until the icon has been
/// created at least once.</para>
/// <para>A write on its own does not move the icon: the shell reads the setting when the icon is
/// registered, so the icon has to be registered again afterwards.
/// <see cref="TrayHost.AskForPlacement"/> does both.</para>
/// </remarks>
public static class NotifyIconSettings
{
    /// <summary>Where the shell draws the icon with this identity, or null when the shell keeps no
    /// entry for it.</summary>
    public static TrayIconPlacement? Read(Guid icon)
    {
        foreach (var setting in ReadAll())
            if (setting.Icon == icon) return setting.Placement;
        return null;
    }

    /// <summary>
    /// Writes the placement for the icon with this identity. Returns whether anything was written:
    /// false means the entry already read as wanted, or the shell keeps no entry that identity
    /// alone picks out.
    /// </summary>
    public static bool Write(Guid icon, TrayIconPlacement wanted)
    {
        if (TrayIconPlacements.Plan(ReadAll(), icon, wanted) is not { } plan) return false;

        using var entry = Registry.CurrentUser.OpenSubKey(
            TrayIconPlacements.SettingsKey + "\\" + plan.Entry, writable: true);
        if (entry is null) return false;

        entry.SetValue(TrayIconPlacements.IsPromotedValue, plan.IsPromoted, RegistryValueKind.DWord);
        return true;
    }

    /// <summary>Every entry the shell keeps that names an icon. An entry whose identity cannot be
    /// read is left out rather than guessed at, so it can never be the one a write lands on.</summary>
    private static List<TrayIconSetting> ReadAll()
    {
        var settings = new List<TrayIconSetting>();
        using var key = Registry.CurrentUser.OpenSubKey(TrayIconPlacements.SettingsKey);
        if (key is null) return settings;

        foreach (string name in key.GetSubKeyNames())
        {
            using var entry = key.OpenSubKey(name);
            if (entry?.GetValue(TrayIconPlacements.IconGuidValue) is not string text) continue;
            if (!Guid.TryParse(text, out Guid icon)) continue;

            settings.Add(new TrayIconSetting(
                name, icon, TrayIconPlacements.FromRegistryValue(entry.GetValue(TrayIconPlacements.IsPromotedValue))));
        }

        return settings;
    }
}
