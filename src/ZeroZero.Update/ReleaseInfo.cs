namespace ZeroZero.Update;

/// <summary>One published release, as the releases API describes it.</summary>
/// <param name="TagName">The tag as published, <c>v1.2.3</c>.</param>
/// <param name="Version">The tag as a version, four parts, for comparison.</param>
/// <param name="VersionText">The tag without its <c>v</c>, as the installer file name carries it.</param>
/// <param name="Body">The release notes, markdown, which also carry the installer's SHA-256.</param>
public sealed record ReleaseInfo(
    string TagName,
    Version Version,
    string VersionText,
    string? Name,
    string Body,
    Uri? HtmlUri,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The asset of exactly that name, or null.</summary>
    public ReleaseAsset? FindAsset(string fileName) =>
        Assets.FirstOrDefault(asset => string.Equals(asset.Name, fileName, StringComparison.Ordinal));
}

/// <param name="Size">The size the release declares, or zero when it declares none.</param>
public sealed record ReleaseAsset(string Name, long Size, Uri DownloadUri);
