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
    /// Invoked when the user clicks "Check for Updates". Leave <see langword="null"/> to hide the
    /// button entirely — e.g. a build with no update channel.
    /// </summary>
    public Func<Task>? OnCheckForUpdates { get; init; }

    /// <summary>
    /// Fired before an update-triggered close, so the host app can shut itself down cleanly ahead
    /// of an installer relaunch (e.g. so no elevated process remains for the installer to kill).
    /// </summary>
    public Action? OnBeforeExit { get; init; }
}
