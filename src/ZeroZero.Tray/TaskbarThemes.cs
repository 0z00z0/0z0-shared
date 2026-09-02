using Microsoft.Win32;

namespace ZeroZero.Tray;

/// <summary>
/// Reads which theme the taskbar is drawn in and decides the stroke tone an icon needs on it.
/// The reading is the personalisation key's system-theme value, not the apps-theme value beside
/// it: an application following the apps setting and drawing dark strokes on a dark taskbar is
/// the measured defect this exists to prevent.
/// </summary>
public static class TaskbarThemes
{
    /// <summary>Where Windows keeps the two theme switches, under the current user.</summary>
    public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>The value that says whether the system surfaces — taskbar, Start — are light.</summary>
    public const string SystemUsesLightThemeValue = "SystemUsesLightTheme";

    /// <summary>The taskbar's theme now. Dark when the value is absent, which is what a fresh
    /// Windows shows.</summary>
    public static TaskbarTheme Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return FromRegistryValue(key?.GetValue(SystemUsesLightThemeValue));
    }

    /// <summary>The theme a raw registry value means: a DWORD of 1 is light, and anything else —
    /// zero, absent, or a value of another kind — is dark.</summary>
    public static TaskbarTheme FromRegistryValue(object? value) =>
        value is int and 1 ? TaskbarTheme.Light : TaskbarTheme.Dark;

    /// <summary>The stroke tone that reads on a taskbar of the given theme: dark strokes on a
    /// light taskbar, light strokes on a dark one.</summary>
    public static StrokeTone StrokeToneFor(TaskbarTheme theme) =>
        theme == TaskbarTheme.Light ? StrokeTone.Dark : StrokeTone.Light;
}
