namespace ZeroZero.Lifecycle;

/// <summary>Where an application keeps its per-user files: the roaming application-data folder
/// under the product name. Depends on nothing else here, so a log file can be placed before
/// anything else exists.</summary>
public static class ProductDataPath
{
    /// <summary>The product's folder, created when absent so a path composed from it is writable
    /// at once.</summary>
    public static string Root(string productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), productName);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>A file or folder under the product's folder. A rooted path is refused rather than
    /// silently replacing the folder, which is what combining would do.</summary>
    public static string Under(string productName, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("A path under the product folder is relative; a rooted path would leave the folder.", nameof(relativePath));

        return Path.Combine(Root(productName), relativePath);
    }
}
