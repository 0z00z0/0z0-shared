using Xunit;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// One real pack of the primitives component by the repository's own packing script, published to
/// a flat folder feed the way the release publishes to GitHub Packages. Shared by every test that
/// needs an artefact the build actually produced. Needs the solution built in this assembly's
/// configuration first: the packing script packs with --no-build, as the release does.
/// </summary>
public sealed class PackedRelease : IDisposable
{
    public const string Key = "primitives";
    public const string PackageId = "ZeroZero.Primitives";

    public string Root { get; }
    public string Version { get; }
    public string Tag => $"{Key}-v{Version}";
    public string Commit { get; }
    public string BuildDirectory { get; }
    public string RecordPath => Path.Combine(BuildDirectory, "release-artefacts.json");
    public string PackageName => $"{PackageId}.{Version}.nupkg";
    public string PackagePath => Path.Combine(BuildDirectory, PackageName);
    public string Feed { get; }
    public ScriptResult PackResult { get; }

    public PackedRelease()
    {
        Root = Path.Combine(Path.GetTempPath(), "zz-release-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
        Version = ReadDeclaredVersion();
        Commit = Git("rev-parse", "HEAD");
        BuildDirectory = Path.Combine(Root, "build");
        PackResult = Pack(BuildDirectory);
        if (!PackResult.Passed)
        {
            throw new InvalidOperationException(
                $"pack-component.ps1 failed; build the solution in {Scripts.Configuration} first. {PackResult}");
        }
        Feed = Path.Combine(Root, "feed");
        Publish(PackagePath, Feed);
    }

    public ScriptResult Pack(string output) =>
        Scripts.Run("pack-component.ps1", null, "-Tag", Tag, "-Output", output, "-Configuration", Scripts.Configuration);

    /// <summary>Publishes a package to a folder feed with dotnet nuget push, as the release does to its feed.</summary>
    public static void Publish(string package, string feed)
    {
        Directory.CreateDirectory(feed);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "nuget", "push", package, "--source", feed })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet nuget push failed: {output.Result}{error.Result}");
        }
    }

    public string NewDirectory(string purpose)
    {
        var path = Path.Combine(Root, purpose + "-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ReadDeclaredVersion()
    {
        var props = XDocument.Load(Path.Combine(Scripts.RepoRoot, "Versions.props"));
        var value = props.Root?.Elements("PropertyGroup").Elements("PrimitivesVersion").FirstOrDefault()?.Value.Trim();
        return value ?? throw new InvalidOperationException("Versions.props declares no PrimitivesVersion.");
    }

    private static string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(Scripts.RepoRoot);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(' ', arguments) + " failed.");
        return output;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

[CollectionDefinition(Name)]
public sealed class PackedReleaseCollection : ICollectionFixture<PackedRelease>
{
    public const string Name = "packed release";
}
