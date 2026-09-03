using System.Xml.Linq;
using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>The packaged readme against the projects that are packed.</summary>
/// <remarks>
/// <para><c>PACKAGE.md</c> is what a consumer reads on the feed, and <c>Directory.Build.props</c>
/// packs it into every package — so a package added without a row in it ships inside every one of
/// them describing a family it is not in. Three branches added two packages between them and none
/// of them touched the file, which is why this is a test rather than a habit.</para>
/// <para>Both directions are checked. A packed project with no row is the case that happened; a row
/// naming a package that no longer exists is the one that happens next, when a project is renamed
/// and the table is left describing the old name.</para>
/// </remarks>
public sealed class PackagedReadmeTests
{
    private static readonly string PackageReadme = Path.Combine(Scripts.RepoRoot, "PACKAGE.md");

    [Fact]
    public void Every_packed_project_has_a_row_in_the_packaged_readme()
    {
        var listed = Listed();
        var missing = Packed().Where(name => !listed.Contains(name)).Order().ToArray();

        Assert.True(
            missing.Length == 0,
            $"PACKAGE.md has no row for {string.Join(", ", missing)}. It is packed into every package, so a consumer reads it.");
    }

    [Fact]
    public void The_packaged_readme_names_no_package_that_is_not_packed()
    {
        var packed = Packed();
        var stale = Listed().Where(name => !packed.Contains(name)).Order().ToArray();

        Assert.True(
            stale.Length == 0,
            $"PACKAGE.md has a row for {string.Join(", ", stale)}, which no project under src/ packs.");
    }

    /// <summary>Every assembly name a project under <c>src/</c> packs. The harness is excluded by
    /// what it declares, never by its name.</summary>
    private static HashSet<string> Packed()
    {
        var projects = Directory.EnumerateFiles(Path.Combine(Scripts.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !XDocument.Load(path).Descendants("IsPackable")
                .Any(static node => string.Equals(node.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileNameWithoutExtension)
            .Select(static name => name!)
            .ToHashSet(StringComparer.Ordinal);

        // A repository with nothing packable would pass both assertions above without meaning it.
        Assert.NotEmpty(projects);
        return projects;
    }

    /// <summary>Every package the table's first column names, read from the file as it ships.</summary>
    private static HashSet<string> Listed()
    {
        var rows = File.ReadAllLines(PackageReadme)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("| `ZeroZero.", StringComparison.Ordinal))
            .Select(static line => line.Split('|', StringSplitOptions.TrimEntries)[1].Trim('`'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(rows);
        return rows;
    }
}
