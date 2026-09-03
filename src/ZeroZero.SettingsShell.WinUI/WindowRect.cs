namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// A window's outer rectangle in physical pixels — position and size as the window manager
/// reports them — the form an application keeps between runs. Plain numbers, so a settings
/// document holds four integers and nothing framework-shaped.
/// </summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height);
