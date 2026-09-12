using System.IO.Compression;
using System.Text.RegularExpressions;
using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// One real pack of the brand component, so the font URIs the markup names can be held to the layout
/// the package actually carries rather than to another sentence about it.
/// </summary>
public sealed class PackedBrand : IDisposable
{
    public const string Key = "brand";
    public const string PackageId = "ZeroZero.Brand.WinUI";

    private readonly string root;

    /// <summary>Every entry in the WinUI package, forward-slashed as a zip carries them.</summary>
    public IReadOnlyList<string> Entries { get; }

    /// <summary>
    /// The folder inside the package that a consuming WinUI build resolves this library's assets
    /// from, read from the package rather than restated: the folder holding the assembly.
    /// </summary>
    public string AssemblyFolder { get; }

    public PackedBrand()
    {
        root = Path.Combine(Path.GetTempPath(), "zz-brand-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        string version = Repository.Components[Key].Version;
        var result = Scripts.Run(
            "pack-component.ps1", null,
            "-Tag", $"{Key}-v{version}", "-Output", root, "-Configuration", Scripts.Configuration);
        if (!result.Passed)
        {
            throw new InvalidOperationException(
                $"pack-component.ps1 failed for {Key}; build the solution in {Scripts.Configuration} first. {result}");
        }

        string package = Path.Combine(root, $"{PackageId}.{version}.nupkg");
        using var archive = ZipFile.OpenRead(package);
        Entries = archive.Entries.Select(static entry => entry.FullName.Replace('\\', '/')).ToArray();

        string assembly = Entries.SingleOrDefault(static entry =>
                              entry.StartsWith("lib/", StringComparison.Ordinal) &&
                              entry.EndsWith($"/{PackageId}.dll", StringComparison.Ordinal))
                          ?? throw new InvalidOperationException(
                              $"{package} carries no lib/<tfm>/{PackageId}.dll, so there is no folder to resolve assets against.");

        AssemblyFolder = assembly[..^$"{PackageId}.dll".Length];
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

[CollectionDefinition(Name)]
public sealed class PackedBrandCollection : ICollectionFixture<PackedBrand>
{
    public const string Name = "packed brand";
}

/// <summary>
/// The brand markup names its typeface by URI, and nothing at runtime reports a URI that resolves to
/// no file: a font that fails to load falls back to the family name, which on a machine with the same
/// face installed is indistinguishable from success. So the two ends are held to each other here.
///
/// A package reference lays this library's content out under the assembly's own folder and puts
/// nothing at the consuming application's output root, which is why a root-relative URI works under a
/// project reference and silently resolves to nothing under a package reference.
/// </summary>
[Collection(PackedBrandCollection.Name)]
public sealed class BrandFontPathTests(PackedBrand brand)
{
    private const string Scheme = "ms-appx:///";

    /// <summary>The markup files that name the face, and the number of URIs each must contribute.</summary>
    private static readonly (string File, int Count)[] Sources =
    [
        (Path.Combine("src", "ZeroZero.Brand.WinUI", "BrandAboutControl.xaml"), 1),
        (Path.Combine("src", "ZeroZero.Brand.WinUI", "BrandAboutWindow.xaml"), 1),
        (Path.Combine("src", "ZeroZero.Brand.WinUI", "Themes", "BrandResources.xaml"), 1),
    ];

    [Fact]
    public void Every_font_uri_in_the_markup_names_a_file_the_package_carries()
    {
        var named = FontUris();

        foreach (var (file, uri) in named)
        {
            string entry = brand.AssemblyFolder + uri;
            Assert.True(
                brand.Entries.Contains(entry, StringComparer.Ordinal),
                $"{file} asks for the brand face at {Scheme}{uri}, and the package carries no {entry}. " +
                $"Under a package reference that URI resolves to nothing and the face silently falls back. " +
                $"The package's font entries are: {string.Join(", ", FontEntries())}");
        }
    }

    [Fact]
    public void Every_font_the_package_carries_is_the_one_the_markup_asks_for()
    {
        var asked = FontUris().Select(static named => named.Uri).ToHashSet(StringComparer.Ordinal);

        foreach (string entry in FontEntries())
        {
            string uri = entry[brand.AssemblyFolder.Length..];
            Assert.True(
                asked.Contains(uri),
                $"The package carries {entry} but no markup asks for {Scheme}{uri}. " +
                $"A font that ships unreferenced means the markup is pointing somewhere else. " +
                $"The markup asks for: {string.Join(", ", asked)}");
        }
    }

    [Fact]
    public void The_markup_is_read_rather_than_matched_against_nothing()
    {
        var found = FontUris();

        foreach (var (file, count) in Sources)
        {
            Assert.Equal(count, found.Count(one => one.File == file));
        }
        Assert.NotEmpty(FontEntries());
    }

    /// <summary>Every font URI the brand markup names, by the file that names it, path only.</summary>
    private static List<(string File, string Uri)> FontUris()
    {
        var found = new List<(string, string)>();

        foreach (var (file, _) in Sources)
        {
            string text = File.ReadAllText(Path.Combine(Scripts.RepoRoot, file));
            foreach (Match match in Regex.Matches(text, @"ms-appx:///([^""<#]+\.ttf)"))
            {
                found.Add((file, match.Groups[1].Value));
            }
        }

        return found;
    }

    /// <summary>The font files the package carries under the folder assets resolve from.</summary>
    private IEnumerable<string> FontEntries() =>
        brand.Entries.Where(entry =>
            entry.StartsWith(brand.AssemblyFolder, StringComparison.Ordinal) &&
            entry.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
}
