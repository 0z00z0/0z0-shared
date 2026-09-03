namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// Where the settings window's rectangle is kept between runs. The application's own settings
/// document, behind two members: the window asks once as it opens and tells once as it closes,
/// and never sees the document. Anything either member throws comes straight back to the
/// application, which owns the store.
/// </summary>
public interface IWindowRectStore
{
    /// <summary>The rectangle saved last time, or null when there is none yet.</summary>
    WindowRect? Load();

    /// <summary>The rectangle to open at next time: never a maximised or minimised one.</summary>
    void Save(WindowRect rect);
}
