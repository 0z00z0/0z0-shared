namespace ZeroZero.Tray.Tests;

/// <summary>Where the interactive harness's executable is, found rather than assumed: the
/// repository root is the nearest ancestor of this assembly holding the solution file, the
/// configuration is the one this assembly was built in, and the runtime folder beneath them is
/// whichever the harness's build chose from the machine's architecture.</summary>
internal static class HarnessLocator
{
    private const string Solution = "0z0-shared.slnx";
    private const string HarnessProject = "ZeroZero.Brand.WinUI.TestHarness";

    public static string Find()
    {
        string assembly = typeof(HarnessLocator).Assembly.Location;
        string configuration = ConfigurationOf(assembly);
        string root = RepositoryRootAbove(assembly);

        string bin = Path.Combine(root, "src", HarnessProject, "bin", configuration);
        var executable = Directory.Exists(bin)
            ? new DirectoryInfo(bin).EnumerateFiles(HarnessProject + ".exe", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;

        return executable?.FullName
            ?? throw new FileNotFoundException($"The harness is not built in {configuration}: build the solution first. Looked under {bin}.");
    }

    private static string ConfigurationOf(string assemblyPath)
    {
        // bin\<Configuration>\<TargetFramework>\<assembly>: the configuration is the folder after bin.
        string[] parts = assemblyPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int bin = Array.FindLastIndex(parts, p => p.Equals("bin", StringComparison.OrdinalIgnoreCase));
        return bin >= 0 && bin + 1 < parts.Length ? parts[bin + 1] : "Release";
    }

    private static string RepositoryRootAbove(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(path)!); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Solution))) return dir.FullName;

        throw new DirectoryNotFoundException($"No {Solution} above {path}.");
    }
}
