using System.Text.RegularExpressions;
using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// The adopting guide's component table, and the rule under it, against the build.
/// </summary>
/// <remarks>
/// <para>The table is the first thing an application reads: what exists, and the one project to
/// reference for each. A row for a component the repository does not release, a component released
/// with no row, or a row naming a project that cannot be referenced each send an adopter somewhere
/// that is not there.</para>
/// <para>The rule under the table is sharper than the table. It tells a reader to take a foundation
/// reference directly only where nothing else brought it, which is safe exactly as long as the
/// guide names the foundation assemblies nothing brings. One that is missing from that sentence is
/// an assembly an adopter is told it already has and does not.</para>
/// </remarks>
public sealed class AdoptingGuideTests
{
    private static readonly string Guide = Path.Combine(Scripts.RepoRoot, "docs", "adopting.md");

    /// <summary>A cell linking a component's own guide, which is what marks a component-table row.</summary>
    private static readonly Regex GuideLink = new(@"\[[^\]]+\]\(zerozero-([a-z0-9.]+)\.md\)");

    /// <summary>An assembly named in the guide, which is always in backticks.</summary>
    private static readonly Regex Named = new(@"`(ZeroZero\.[A-Za-z0-9.]+)`");

    [Fact]
    public void Every_component_the_versions_file_declares_has_a_row()
    {
        var rows = Rows();
        var missing = Repository.Components.Keys.Where(key => !rows.ContainsKey(key)).Order().ToArray();

        Assert.True(
            missing.Length == 0,
            $"docs/adopting.md has no row for {string.Join(", ", missing)}, which Versions.props declares and the repository releases on its own.");
    }

    [Fact]
    public void The_table_names_no_component_the_versions_file_does_not_declare()
    {
        var stale = Rows().Keys.Where(key => !Repository.Components.ContainsKey(key)).Order().ToArray();

        Assert.True(
            stale.Length == 0,
            $"docs/adopting.md has a row for {string.Join(", ", stale)}, which Versions.props does not declare, so nothing releases it and there is no version to take.");
    }

    /// <summary>
    /// The row's key is read off the guide it links, so a link to a guide that is not there would
    /// invent a component out of a broken link.
    /// </summary>
    [Fact]
    public void Every_row_links_a_guide_that_exists()
    {
        var absent = Rows().Keys
            .Where(key => !File.Exists(Path.Combine(Scripts.RepoRoot, "docs", $"zerozero-{key}.md")))
            .Order()
            .ToArray();

        Assert.True(absent.Length == 0, $"docs/adopting.md links a guide for {string.Join(", ", absent)} that is not in docs/.");
    }

    [Fact]
    public void Every_reference_the_table_names_is_a_project_that_can_be_referenced()
    {
        var wrong = Rows().Values
            .SelectMany(static row => row.References.Select(reference => (row.Key, reference)))
            .Where(static named => !Repository.Projects.TryGetValue(named.reference, out var project) || !project.Packable)
            .Select(static named => $"{named.Key} names {named.reference}")
            .Order()
            .ToArray();

        Assert.True(
            wrong.Length == 0,
            $"docs/adopting.md tells an adopter to reference a project that is not under src/ or does not pack: {string.Join("; ", wrong)}.");
    }

    /// <summary>
    /// A row's reference belongs to that row's component. A row pointing at another component's
    /// assembly reads as one version to take and is a second one, which is how a mixture of
    /// versions gets into a consumer.
    /// </summary>
    [Fact]
    public void Every_reference_the_table_names_belongs_to_the_component_of_its_row()
    {
        var elsewhere = Rows().Values
            .SelectMany(static row => row.References.Select(reference => (row.Key, reference)))
            .Where(static named =>
                Repository.Projects.TryGetValue(named.reference, out var project) &&
                project.Component is not null &&
                project.Component != named.Key)
            .Select(static named => $"{named.Key} names {named.reference}, which is versioned as {Repository.Projects[named.reference].Component}")
            .Order()
            .ToArray();

        Assert.True(elsewhere.Length == 0, $"docs/adopting.md puts a reference under the wrong component: {string.Join("; ", elsewhere)}.");
    }

    /// <summary>
    /// A row names no reference only where the component packs no assembly to reference — the build
    /// kit, whose package carries MSBuild files and no library. Both directions: a component that
    /// does pack one has a row that names it.
    /// </summary>
    [Fact]
    public void A_row_names_no_reference_only_where_the_component_packs_no_assembly()
    {
        var silent = Rows().Values
            .Where(static row => row.References.Count == 0 && ComponentPacksAnAssembly(row.Key))
            .Select(static row => row.Key)
            .Order()
            .ToArray();

        Assert.True(
            silent.Length == 0,
            $"docs/adopting.md names no reference for {string.Join(", ", silent)}, which packs an assembly an adopter is meant to reference.");
    }

    [Fact]
    public void A_component_that_packs_no_assembly_is_not_written_up_as_a_reference()
    {
        var referenced = Rows().Values
            .Where(static row => row.References.Count > 0 && !ComponentPacksAnAssembly(row.Key))
            .Select(static row => row.Key)
            .Order()
            .ToArray();

        Assert.True(
            referenced.Length == 0,
            $"docs/adopting.md names a reference for {string.Join(", ", referenced)}, whose package carries no assembly to reference.");
    }

    /// <summary>
    /// The flagship. Every foundation assembly that no other component's reference brings is named
    /// where the rule is stated, because the rule tells a reader to skip the ones that arrive on
    /// their own. An assembly missing here is one an adopter never references and never gets.
    /// </summary>
    [Fact]
    public void Every_foundation_assembly_that_nothing_brings_is_named_where_the_rule_is_stated()
    {
        var named = NamedAsDirect();
        var missing = ArriveWithNothing().Where(assembly => !named.Contains(assembly)).Order().ToArray();

        Assert.True(
            missing.Length == 0,
            $"No reference in docs/adopting.md brings {string.Join(", ", missing)}, and the rule under the table does not name it, so an adopter following the rule takes it from nothing and does not have it.");
    }

    /// <summary>
    /// The other half. An assembly named there that something does bring is a direct reference the
    /// rule's own exception asks for and nothing needs — the mixture the guide warns about, taken
    /// on the guide's own instruction.
    /// </summary>
    [Fact]
    public void The_rule_names_no_foundation_assembly_that_another_reference_brings()
    {
        var arriveWithNothing = ArriveWithNothing();
        var spurious = NamedAsDirect().Where(assembly => !arriveWithNothing.Contains(assembly)).Order().ToArray();

        Assert.True(
            spurious.Length == 0,
            $"The rule under the table in docs/adopting.md asks for a direct reference to {string.Join(", ", spurious)}, which another component's reference already brings.");
    }

    /// <summary>Every foundation assembly no other component's packable project brings.</summary>
    private static IReadOnlySet<string> ArriveWithNothing()
    {
        Assert.NotEmpty(Repository.Foundation);

        return Repository.Projects.Values
            .Where(static project => project.Packable && project.Component is not null)
            .Where(static project => Repository.Foundation.Contains(project.Component!))
            .Where(static project => !Repository.Bringers.ContainsKey(project.Name))
            .Select(static project => project.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The assemblies the rule names. The rule is the prose the table is followed by, and it is the
    /// only place in the guide that answers which foundation references an adopter still has to
    /// take; the paragraph is held to mentioning foundation at all so a restructure fails here
    /// rather than passing on an empty read.
    /// </summary>
    private static IReadOnlySet<string> NamedAsDirect()
    {
        string rule = RuleUnderTheTable();

        Assert.Contains("foundation", rule, StringComparison.Ordinal);

        return Named.Matches(rule).Select(static match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The first paragraph after the component table.</summary>
    private static string RuleUnderTheTable()
    {
        var lines = File.ReadAllLines(Guide);
        int last = Array.FindLastIndex(lines, static line => GuideLink.IsMatch(line) && line.TrimStart().StartsWith('|'));

        Assert.True(last >= 0, "docs/adopting.md has no component table, so the rule under it cannot be found.");

        var paragraph = lines.Skip(last + 1).SkipWhile(static line => line.Trim().Length == 0).TakeWhile(static line => line.Trim().Length > 0).ToArray();

        Assert.NotEmpty(paragraph);
        return string.Join(' ', paragraph);
    }

    private static bool ComponentPacksAnAssembly(string component) =>
        Repository.Projects.Values.Any(project => project.Component == component && project.Packable && project.PacksAnAssembly);

    /// <summary>Every row of the component table, by the component its guide link names.</summary>
    private static Dictionary<string, TableRow> Rows()
    {
        var rows = new Dictionary<string, TableRow>(StringComparer.Ordinal);

        foreach (string line in File.ReadAllLines(Guide).Select(static line => line.Trim()))
        {
            if (!line.StartsWith('|')) continue;

            var cells = line.Split('|');
            int guide = Array.FindIndex(cells, static cell => GuideLink.IsMatch(cell));
            if (guide < 1) continue;

            string key = GuideLink.Match(cells[guide]).Groups[1].Value;
            var references = Named.Matches(cells[guide - 1]).Select(static match => match.Groups[1].Value).ToArray();

            rows[key] = new TableRow(key, references);
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    private sealed record TableRow(string Key, IReadOnlyList<string> References);
}
