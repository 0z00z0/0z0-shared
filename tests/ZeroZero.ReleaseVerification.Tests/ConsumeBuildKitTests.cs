using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// The self-contained trap in the build-kit replacement checklist, against the projects that take
/// the Windows App SDK.
/// </summary>
/// <remarks>
/// <para>The trap tells a consuming application that a self-contained property set globally fails
/// the build of every library it references, and counts the library components here that take the
/// package to say how far that reaches. The count is the part a reader acts on: it is why the fix
/// is on the referencing edge or in the application, and not a change to one project here.</para>
/// <para>The number is read out of the document however it is written and compared with the
/// projects, so rewording the sentence leaves the guard standing while adding or dropping a package
/// reference does not. The second half holds the word the count is stated in: an application counted
/// among the libraries would keep the number right and the sentence wrong.</para>
/// </remarks>
public sealed class ConsumeBuildKitTests
{
    private static readonly string Checklist =
        Path.Combine(Scripts.RepoRoot, "docs", "consume-build-kit.md");

    /// <summary>The package whose targets refuse the self-contained property on a library.</summary>
    private const string WindowsAppSdk = "Microsoft.WindowsAppSDK";

    private static readonly string[] NumberWords =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty"
    ];

    /// <summary>A count of libraries, spelt or in digits, which is the claim being held to the build.</summary>
    private static readonly Regex Stated = new(
        @"\b(?<count>" + string.Join('|', NumberWords) + @"|\d+)\s+librar(?:y|ies)\b",
        RegexOptions.IgnoreCase);

    [Fact]
    public void The_trap_counts_the_library_components_that_take_the_windows_app_sdk()
    {
        var components = CountedComponents();
        int stated = StatedCount();

        Assert.True(
            stated == components.Count,
            $"docs/consume-build-kit.md says {stated} library components take {WindowsAppSdk}, and {components.Count} do: {string.Join(", ", components.Order())}. A reader takes the count as the reach of the failure and strips the property for that many.");
    }

    /// <summary>
    /// The other half. Everything the count is made of builds a library, which is what makes the
    /// package refuse it. An application among them would leave the number right and the sentence
    /// false, and the number alone cannot tell the difference.
    /// </summary>
    [Fact]
    public void Everything_the_count_is_made_of_builds_a_library()
    {
        var applications = Counted()
            .Where(static project => !project.IsLibrary)
            .Select(static project => $"{project.Name} builds {project.OutputType}")
            .Order()
            .ToArray();

        Assert.True(
            applications.Length == 0,
            $"docs/consume-build-kit.md counts {string.Join(", ", applications)} among the library components taking {WindowsAppSdk}, and the package refuses the property on a library rather than on an application.");
    }

    /// <summary>The count the checklist states, whether it is spelt or written in digits.</summary>
    private static int StatedCount()
    {
        var matches = Stated.Matches(File.ReadAllText(Checklist));

        Assert.True(
            matches.Count == 1,
            $"docs/consume-build-kit.md states {matches.Count} counts of libraries where the self-contained trap makes exactly one, so there is no single claim to hold to the reference graph.");

        string count = matches[0].Groups["count"].Value;
        int word = Array.FindIndex(NumberWords, name => name.Equals(count, StringComparison.OrdinalIgnoreCase));

        return word >= 0 ? word : int.Parse(count, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Every project the count is made of: one belonging to a released component, taking the package.
    /// Fails closed on an import that resolves to nothing, because the output kind is read through
    /// imports and an unread import would classify an application as a library.
    /// </summary>
    private static IReadOnlyList<SourceProject> Counted()
    {
        Assert.True(
            Repository.UnresolvableImports.Count == 0,
            $"A project imports a file that is not in the repository, so what that file sets cannot be read: {string.Join("; ", Repository.UnresolvableImports)}.");

        var counted = Repository.Projects.Values
            .Where(static project => project.Takes(WindowsAppSdk))
            .Where(static project => project.Packable && project.Component is not null)
            .ToArray();

        Assert.NotEmpty(counted);
        return counted;
    }

    /// <summary>The components those projects belong to, which is what the trap counts.</summary>
    private static IReadOnlySet<string> CountedComponents() =>
        Counted().Select(static project => project.Component!).ToHashSet(StringComparer.Ordinal);
}
