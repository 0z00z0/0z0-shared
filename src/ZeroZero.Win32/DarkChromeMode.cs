namespace ZeroZero.Win32;

/// <summary>How the process's native chrome follows the system theme. The values are the
/// preferred-app-mode ordinals uxtheme takes.</summary>
public enum DarkChromeMode
{
    /// <summary>Light, whatever the system setting.</summary>
    Default = 0,
    /// <summary>Dark when the system theme is dark.</summary>
    AllowDark = 1,
    /// <summary>Dark, whatever the system setting.</summary>
    ForceDark = 2,
    /// <summary>Light, whatever the system setting.</summary>
    ForceLight = 3,
}
