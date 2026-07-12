namespace ZeroZero.Brand.Core;

/// <summary>A third-party dependency to credit in an About surface's "External libraries" list.</summary>
/// <param name="Name">Library/package name.</param>
/// <param name="Author">Author or maintaining org.</param>
/// <param name="Purpose">One-line description of what it's used for.</param>
/// <param name="License">License identifier (e.g. "MIT").</param>
/// <param name="Url">Optional link to the project's homepage/repo, for a clickable credit.</param>
public sealed record ExternalLibrary(string Name, string Author, string Purpose, string License, string? Url = null);
