using ZeroZero.Brand.Core;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// The parameters for a <see cref="BrandAboutWindow"/> — data only, no per-app XAML or logic
/// duplication. Each consuming app supplies its own <see cref="AboutInfo"/> plus, optionally, a
/// callback wrapping its *own* existing update-check flow; the shared window only owns chrome
/// and layout.
/// </summary>
public sealed record BrandAboutOptions
{
    public required AboutInfo Info { get; init; }

    /// <summary>
    /// Invoked when the user clicks "Check for Updates" — runs the host app's own update flow.
    /// Return <see langword="true"/> if an update was applied and the app should now exit so the
    /// installer can relaunch it; <see langword="false"/> keeps the About window open. Leave
    /// <see langword="null"/> to hide the button entirely (e.g. a build with no update channel).
    /// </summary>
    public Func<Task<bool>>? OnCheckForUpdates { get; init; }

    /// <summary>
    /// Invoked by the window — after <see cref="OnCheckForUpdates"/> reports an update was applied —
    /// to let the host tear itself down cleanly before the installer relaunches it (release a mutex,
    /// stop an elevated child, flush state, …). Return <see langword="true"/> when teardown is done
    /// and the window may close and terminate the app; return <see langword="false"/> to veto the
    /// exit and keep the window open. Leave <see langword="null"/> to simply close the window.
    /// </summary>
    public Func<Task<bool>>? OnBeforeExit { get; init; }
}
