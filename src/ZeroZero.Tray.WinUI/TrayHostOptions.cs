namespace ZeroZero.Tray.WinUI;

/// <summary>What the application supplies to the host: its identity, the icon delegate, the
/// tooltip composer, the menu descriptor and the click actions. Everything else — when the icon
/// is created and refreshed, what the tooltip may hold, what a click means — is the host's.</summary>
public sealed class TrayHostOptions
{
    /// <summary>The icon's name in the shell's icon settings, and the seed of its identity when
    /// <see cref="Id"/> is not given. Stable across versions, or the shell treats each build as a
    /// new icon and forgets whether the user chose to show it.</summary>
    public required string Name { get; init; }

    /// <summary>The icon's identity in the shell; derived from <see cref="Name"/> when not given.</summary>
    public Guid? Id { get; init; }

    /// <summary>The icon for the current state at the requested slot and tone. Called on the UI
    /// thread at start and whenever the request or the state changes; the drawing is the
    /// application's.</summary>
    public required Func<TrayIconRequest, TrayIconImage> Icon { get; init; }

    /// <summary>The tooltip's lines for the current state; the host applies the discipline. No
    /// tooltip when not given.</summary>
    public Func<IEnumerable<TrayTooltipLine>>? Tooltip { get; init; }

    /// <summary>The menu for the current state, asked for each time the menu is about to open. No
    /// menu when not given.</summary>
    public Func<IEnumerable<TrayMenuItem>>? Menu { get; init; }

    /// <summary>What a single left click does.</summary>
    public Action? LeftClick { get; init; }

    /// <summary>What the second click of a double click does; the first has already run
    /// <see cref="LeftClick"/>.</summary>
    public Action? DoubleClick { get; init; }

    /// <summary>Where a render is written; the user's temporary folder when not given.</summary>
    public string? CacheDirectory { get; init; }

    /// <summary>How long after <see cref="TrayHost.NotePopOutDismissed"/> a left click is dropped;
    /// the system's double-click time when not given.</summary>
    public TimeSpan? ReopenGuard { get; init; }
}
