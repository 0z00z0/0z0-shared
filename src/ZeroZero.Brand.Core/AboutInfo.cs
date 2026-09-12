namespace ZeroZero.Brand.Core;

/// <summary>The per-app data an About surface (window or console banner) needs. Everything
/// studio-wide (name, tagline, links, palette) lives in <see cref="Brand"/> instead.</summary>
public sealed record AboutInfo
{
    public required string AppName { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public required string RepoUrl { get; init; }

    /// <summary>
    /// Where the "What's new" notes are fetched from — plain text, short enough to read inside a
    /// small window. Per-app data alongside <see cref="RepoUrl"/>, because each application
    /// publishes its own notes. Leave <see langword="null"/> to hide the button entirely: an
    /// application with nowhere to point gets no dead row.
    /// </summary>
    public string? ReleaseNotesUrl { get; init; }

    public IReadOnlyList<ExternalLibrary> ExternalLibraries { get; init; } = [];
}
