using Microsoft.UI.Xaml;

namespace ZeroZero.Update.WinUI;

/// <summary>
/// What the shared update window needs from the application. The wording of every outcome is the
/// component's, so only the application's own name, its theme and where its release notes come
/// from are settable here.
/// </summary>
public sealed record UpdateWindowOptions
{
    /// <summary>The application's name, shown above the headline and named in the sentence about
    /// closing for the installer.</summary>
    public required string ApplicationName { get; init; }

    /// <summary>The theme the window renders in. Default follows the application; an application
    /// pinned to one theme passes it here, as it does for its title bars.</summary>
    public ElementTheme Theme { get; init; } = ElementTheme.Default;

    /// <summary>
    /// The notes shown under the question, for an application that keeps its own. Null, or a null
    /// return, takes the release body with its markdown stripped; an empty return leaves the notes
    /// panel out.
    /// </summary>
    public Func<ReleaseInfo, string?>? ReleaseNotes { get; init; }
}
