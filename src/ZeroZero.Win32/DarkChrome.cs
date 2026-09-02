namespace ZeroZero.Win32;

/// <summary>
/// Opts the process's native chrome — context menus above all, the surface a tray application
/// shows most — into the dark theme. Two uxtheme entry points that are reached by ordinal and
/// documented nowhere; there is no supported way to do this.
/// </summary>
public static class DarkChrome
{
    /// <returns>False on a Windows build without the entry points (before 10.0.18362), where
    /// native chrome stays light and nothing else changes.</returns>
    public static bool Apply(DarkChromeMode mode)
    {
        try
        {
            NativeMethods.SetPreferredAppMode((int)mode);
            // Menus already created keep their old theme until the cache is dropped.
            NativeMethods.FlushMenuThemes();
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
