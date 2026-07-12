namespace ZeroZero.Brand.Core;

/// <summary>The per-app data an About surface (window or console banner) needs. Everything
/// studio-wide (name, tagline, links, palette) lives in <see cref="Brand"/> instead.</summary>
public sealed record AboutInfo
{
    public required string AppName { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public required string RepoUrl { get; init; }
    public IReadOnlyList<ExternalLibrary> ExternalLibraries { get; init; } = [];
}
