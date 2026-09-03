using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>The build guide's guard table and manifest path against the kit itself.</summary>
/// <remarks>
/// <para>The guard table is what a consumer reads to find out which build errors it can meet and
/// what each one means. A code the kit raises and the table omits leaves a build failing with an
/// error nothing explains; a row for a code the kit no longer raises is worse, because it is
/// specific and wrong.</para>
/// <para>Three of the guards fire only under the WinUI application block, and the table said so of
/// none of them. A library project cannot meet those codes at all, so a row that reads
/// unconditionally sends its reader looking in the wrong place.</para>
/// </remarks>
public sealed class BuildKitGuideTests
{
    private static readonly string Guide = Path.Combine(Scripts.RepoRoot, "docs", "zerozero-build.md");

    private static readonly string Kit =
        Path.Combine(Scripts.RepoRoot, "src", "ZeroZero.Build", "Sdk", "ZeroZero.Build.targets");

    private static readonly string WinUIBlock =
        Path.Combine(Scripts.RepoRoot, "src", "ZeroZero.Build", "Sdk", "ZeroZero.WinUIApp.props");

    /// <summary>The property the kit's WinUI block sets, which every WinUI-only guard is conditioned
    /// on.</summary>
    private const string WinUIMarker = "ZeroZeroWinUIAppImported";

    [Fact]
    public void The_guide_has_a_row_for_every_guard_the_kit_raises()
    {
        var rows = Rows();
        var missing = Guards().Keys.Where(code => !rows.ContainsKey(code)).Order().ToArray();

        Assert.True(
            missing.Length == 0,
            $"docs/zerozero-build.md has no row for {string.Join(", ", missing)}, which the kit raises.");
    }

    [Fact]
    public void The_guide_names_no_guard_the_kit_does_not_raise()
    {
        var guards = Guards();
        var stale = Rows().Keys.Where(code => !guards.ContainsKey(code)).Order().ToArray();

        Assert.True(
            stale.Length == 0,
            $"docs/zerozero-build.md has a row for {string.Join(", ", stale)}, which the kit does not raise.");
    }

    /// <summary>
    /// A guard that can only fire under the WinUI block says so in its row. Stated the way the table
    /// already states it for ZZB004 — the row names the block — so the test asks for the phrase and
    /// not for a form of words.
    /// </summary>
    [Fact]
    public void Every_guard_that_only_fires_under_the_WinUI_block_says_so_in_its_row()
    {
        var rows = Rows();
        var unmarked = Guards()
            .Where(guard => guard.Value && rows.TryGetValue(guard.Key, out var row) && !SaysWinUI(row))
            .Select(guard => guard.Key)
            .Order()
            .ToArray();

        Assert.True(
            unmarked.Length == 0,
            $"docs/zerozero-build.md states {string.Join(", ", unmarked)} unconditionally, and the kit raises each only under the WinUI application block. A library project cannot meet them.");
    }

    /// <summary>
    /// A guard the kit raises anywhere is not marked as the WinUI block's, which would send a
    /// consumer looking for an import it does not have. Both halves are needed: without this one,
    /// marking every row would pass the test above.
    /// </summary>
    [Fact]
    public void No_guard_that_fires_anywhere_is_written_up_as_the_WinUI_blocks()
    {
        var rows = Rows();
        var overmarked = Guards()
            .Where(guard => !guard.Value && rows.TryGetValue(guard.Key, out var row) && SaysWinUI(row))
            .Select(guard => guard.Key)
            .Order()
            .ToArray();

        Assert.True(
            overmarked.Length == 0,
            $"docs/zerozero-build.md ties {string.Join(", ", overmarked)} to the WinUI application block, and the kit raises it whatever a project imports.");
    }

    /// <summary>
    /// The path the guide quotes for the generated manifest, against the property the kit writes it
    /// to. The property is composed from the project extensions path, which is <c>obj\</c> unless a
    /// repository has moved it, so the guide quotes the tail and this holds the guide to it.
    /// </summary>
    [Fact]
    public void The_guide_quotes_the_path_the_kit_writes_the_manifest_to()
    {
        string declared = XDocument.Load(WinUIBlock).Descendants()
            .Single(node => node.Name.LocalName == "ZeroZeroGeneratedManifest")
            .Value;

        Assert.Equal(@"$(MSBuildProjectExtensionsPath)ZeroZero.Build\app.manifest", declared);

        string tail = declared.Replace("$(MSBuildProjectExtensionsPath)", @"obj\", StringComparison.Ordinal);

        Assert.Contains(
            "`" + tail + "`",
            File.ReadAllText(Guide),
            StringComparison.Ordinal);
    }

    /// <summary>Every guard code the kit raises, to whether it fires only under the WinUI block.</summary>
    private static Dictionary<string, bool> Guards()
    {
        var guards = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (string file in new[] { Kit, Path.Combine(Scripts.RepoRoot, "src", "ZeroZero.Build", "build", "ZeroZero.Build.targets") })
        {
            var document = XDocument.Load(file);

            foreach (var error in document.Descendants().Where(node => node.Name.LocalName == "Error"))
            {
                string? code = error.Attribute("Code")?.Value;
                if (code is null) continue;

                // The guard's own condition, and the condition of the target it sits in: a guard
                // inside the manifest writer is the WinUI block's whether or not it repeats it.
                string context = (error.Attribute("Condition")?.Value ?? "") +
                                 (error.Parent?.Attribute("Condition")?.Value ?? "");

                guards[code] = context.Contains(WinUIMarker, StringComparison.Ordinal);
            }
        }

        Assert.NotEmpty(guards);
        return guards;
    }

    /// <summary>Every row of the guide's guard table: the code in the first cell to the text of the
    /// second.</summary>
    private static Dictionary<string, string> Rows()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var row = new Regex(@"^\|\s*`(ZZB\d{3})`\s*\|(.+?)\|\s*$");

        foreach (string line in File.ReadAllLines(Guide))
        {
            var match = row.Match(line.Trim());
            if (match.Success) rows[match.Groups[1].Value] = match.Groups[2].Value;
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    /// <summary>Whether a row ties its guard to the WinUI application block.</summary>
    private static bool SaysWinUI(string row) =>
        row.Contains("WinUI", StringComparison.OrdinalIgnoreCase);
}
