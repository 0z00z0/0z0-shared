namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// The colour the window's backdrop is tinted with, one for each theme. Windows tints the backdrop
/// from the wallpaper when nothing is supplied, so a window whose wallpaper is blue turns blue
/// while it is active and matches nothing the application draws. An application that owns a main
/// window supplies that window's own colour here, and the two read as one application.
/// </summary>
public sealed class BackdropTint
{
    /// <summary>The colour used while the window renders light.</summary>
    public required BackdropColour Light { get; init; }

    /// <summary>The colour used while the window renders dark.</summary>
    public required BackdropColour Dark { get; init; }

    /// <summary>
    /// The colour a window opens with, or null to leave the backdrop as Windows tints it. Nothing
    /// supplied is null for either theme: an application that says nothing keeps the behaviour it
    /// had.
    /// </summary>
    public static BackdropColour? Resolve(BackdropTint? tint, bool dark) =>
        tint is null ? null : dark ? tint.Dark : tint.Light;
}
