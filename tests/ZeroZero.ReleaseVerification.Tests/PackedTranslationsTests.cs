using System.IO.Compression;
using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>One real pack of every component with a translation. A package missing its satellite
/// assembly runs in the neutral language only, and nothing reports the loss.</summary>
public sealed class PackedTranslations : IDisposable
{
    private readonly string root;

    /// <summary>Every packable project's translations, as project name and culture, read from its
    /// <c>&lt;name&gt;.&lt;culture&gt;.resx</c> files.</summary>
    public IReadOnlyList<(string Project, string Culture)> Expected { get; }

    /// <summary>Every entry of each translated project's package, by project name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Entries { get; }

    public PackedTranslations()
    {
        root = Path.Combine(Path.GetTempPath(), "zz-translations-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        Expected = Repository.Projects.Values
            .Where(static project => project.Packable && project.Component is not null)
            .SelectMany(static project =>
                Directory.EnumerateFiles(Path.GetDirectoryName(project.File)!, "*.resx", SearchOption.AllDirectories)
                    .Select(static file => Path.GetFileNameWithoutExtension(file).Split('.'))
                    .Where(static parts => parts.Length > 1)
                    .Select(parts => (project.Name, parts[^1])))
            .Distinct()
            .ToArray();

        var entries = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (string key in Expected.Select(static e => Repository.Projects[e.Project].Component!).Distinct())
        {
            string version = Repository.Components[key].Version;
            string output = Path.Combine(root, key);
            var result = Scripts.Run(
                "pack-component.ps1", null,
                "-Tag", $"{key}-v{version}", "-Output", output, "-Configuration", Scripts.Configuration);
            if (!result.Passed)
            {
                throw new InvalidOperationException(
                    $"pack-component.ps1 failed for {key}; build the solution in {Scripts.Configuration} first. {result}");
            }

            foreach (string project in Expected.Select(static e => e.Project).Distinct()
                         .Where(project => Repository.Projects[project].Component == key))
            {
                using var archive = ZipFile.OpenRead(Path.Combine(output, $"{project}.{version}.nupkg"));
                entries[project] = archive.Entries.Select(static entry => entry.FullName.Replace('\\', '/')).ToArray();
            }
        }

        Entries = entries;
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

[CollectionDefinition(Name)]
public sealed class PackedTranslationsCollection : ICollectionFixture<PackedTranslations>
{
    public const string Name = "packed translations";
}

[Collection(PackedTranslationsCollection.Name)]
public sealed class PackedTranslationsTests(PackedTranslations packed)
{
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void Every_translation_in_the_source_is_a_satellite_assembly_in_the_package()
    {
        foreach (var (project, culture) in packed.Expected)
        {
            string satellite = $"/{culture}/{project}.resources.dll";
            Assert.True(
                packed.Entries[project].Any(entry =>
                    entry.StartsWith("lib/", StringComparison.Ordinal) && entry.EndsWith(satellite, StringComparison.Ordinal)),
                $"{project} has a {culture} translation and its package carries no lib/<tfm>{satellite}, " +
                "so a consuming application shows the neutral language only.");
        }
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void The_translations_are_found_rather_than_matched_against_nothing() =>
        Assert.Contains(("ZeroZero.Mqtt", "nb"), packed.Expected);
}
