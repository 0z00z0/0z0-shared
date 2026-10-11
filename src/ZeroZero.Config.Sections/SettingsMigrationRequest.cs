namespace ZeroZero.Config.Sections;

/// <summary>What one migration is asked to do.</summary>
/// <param name="SourcePath">The document to read. It is never written, renamed or deleted.</param>
/// <param name="TargetPath">The document to write. Refused if it already exists.</param>
public sealed record SettingsMigrationRequest(string SourcePath, string TargetPath)
{
    /// <summary>The version the new document declares.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The grouping. An empty list carries every key across at the top level, which is what
    /// a document that is already sectioned needs.</summary>
    public IReadOnlyList<SettingsSectionMove> Moves { get; init; } = [];
}
