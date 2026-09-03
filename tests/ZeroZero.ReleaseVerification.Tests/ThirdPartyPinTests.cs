using System.Xml.Linq;
using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>The consuming guide's pin table against the pins the build kit declares.</summary>
/// <remarks>
/// <para><c>docs/consuming.md</c> tells a consumer that importing the kit resolves exactly the
/// versions in its table, and a consumer that has not taken the kit yet reads that table to find out
/// what it is agreeing to. A pin added to the kit and not to the table makes that sentence false —
/// which is what happened when the watcher's controllable clock arrived — and the version a table
/// carries after a pin has moved is worse still, because it is specific and wrong.</para>
/// <para>Names and versions both, in both directions. The table's other columns are prose and are
/// nobody's business here.</para>
/// </remarks>
public sealed class ThirdPartyPinTests
{
    private static readonly string Guide = Path.Combine(Scripts.RepoRoot, "docs", "consuming.md");

    private static readonly string Pins =
        Path.Combine(Scripts.RepoRoot, "src", "ZeroZero.Build", "Sdk", "ZeroZero.Packages.props");

    [Fact]
    public void The_consuming_guide_lists_every_pin_the_kit_declares()
    {
        var listed = Listed();
        var missing = Declared().Keys.Where(name => !listed.ContainsKey(name)).Order().ToArray();

        Assert.True(
            missing.Length == 0,
            $"docs/consuming.md has no row for {string.Join(", ", missing)}, which the kit pins. A consumer reads that table to learn what importing the kit resolves.");
    }

    [Fact]
    public void The_consuming_guide_names_no_pin_the_kit_does_not_declare()
    {
        var declared = Declared();
        var stale = Listed().Keys.Where(name => !declared.ContainsKey(name)).Order().ToArray();

        Assert.True(
            stale.Length == 0,
            $"docs/consuming.md has a row for {string.Join(", ", stale)}, which the kit does not pin.");
    }

    [Fact]
    public void Every_version_in_the_guide_is_the_version_the_kit_pins()
    {
        var declared = Declared();

        foreach (var (name, quoted) in Listed())
        {
            if (!declared.TryGetValue(name, out var version)) continue;

            Assert.True(
                string.Equals(version, quoted, StringComparison.Ordinal),
                $"docs/consuming.md says {name} is {quoted}; the kit pins it at {version}.");
        }
    }

    /// <summary>Every family pin, name to version, read from the kit itself.</summary>
    private static Dictionary<string, string> Declared()
    {
        var pins = XDocument.Load(Pins).Descendants("ZeroZeroFamilyPin")
            .ToDictionary(
                static node => node.Attribute("Include")!.Value,
                static node => node.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        Assert.NotEmpty(pins);
        return pins;
    }

    /// <summary>Every row of the guide's pin table: the first column's name to the second's version.
    /// A row whose version cell is empty carries the one above it and is skipped.</summary>
    private static Dictionary<string, string> Listed()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(Guide).Select(static line => line.Trim()))
        {
            if (!line.StartsWith("| `", StringComparison.Ordinal)) continue;

            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 4) continue;

            var name = cells[1].Trim('`');
            var version = cells[2].Trim('`');

            // Only the pin table has a bare version in its second cell; every other table in the
            // guide puts prose there, and prose is not a version.
            if (version.Length == 0 || !char.IsAsciiDigit(version[0])) continue;

            rows[name] = version;
        }

        Assert.NotEmpty(rows);
        return rows;
    }
}
