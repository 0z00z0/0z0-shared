using System.Xml.Linq;
using Xunit;


namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// The repository as the build sees it: the component keys <c>Versions.props</c> declares, every
/// project under <c>src/</c>, and what each project drags in behind it. A guide checked against this
/// is checked against the build rather than against another sentence.
/// </summary>
internal static class Repository
{
    private const string VersionSuffix = "Version";

    private static readonly string SourceDirectory = Path.Combine(Scripts.RepoRoot, "src");

    /// <summary>What <c>$(ZeroZeroBuildDir)</c> evaluates to: the build kit's own folder.</summary>
    private static readonly string BuildKitDirectory = Path.Combine(SourceDirectory, "ZeroZero.Build");

    private static readonly List<string> Unresolvable = [];

    /// <summary>Every component, by the lowercase name its tags and its notes folder use.</summary>
    public static IReadOnlyDictionary<string, Component> Components { get; } = ReadComponents();

    /// <summary>Every project under <c>src/</c>, by assembly name.</summary>
    public static IReadOnlyDictionary<string, SourceProject> Projects { get; } = ReadProjects();

    /// <summary>
    /// Imports a project declares that resolve to no file here. Anything read through imports fails
    /// closed on one: a property set in a file that could not be found is a property read as unset.
    /// </summary>
    public static IReadOnlyList<string> UnresolvableImports
    {
        get
        {
            _ = Projects;
            return Unresolvable;
        }
    }

    /// <summary>
    /// Every assembly a reference to <paramref name="project"/> resolves, the project itself apart.
    /// This is what an adopter gets for the one reference: a project reference is transitive, so the
    /// whole chain below it arrives whether or not the adopter names it.
    /// </summary>
    public static IReadOnlySet<string> Closure(string project)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(project);

        while (pending.TryDequeue(out var current))
        {
            if (!Projects.TryGetValue(current, out var source)) continue;

            foreach (var reference in source.ProjectReferences)
            {
                if (reached.Add(reference)) pending.Enqueue(reference);
            }
        }

        reached.Remove(project);
        return reached;
    }

    /// <summary>
    /// Every assembly some other component's packable project brings, to the components that bring
    /// it. An assembly outside this map arrives with nothing and can only be referenced directly.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Bringers { get; } = ReadBringers();

    /// <summary>
    /// The components another component brings — foundation, in the guides' term. Computed rather
    /// than read, so the guide's list of them is held to the reference graph.
    /// </summary>
    public static IReadOnlySet<string> Foundation { get; } =
        Bringers.Keys
            .Select(static brought => Projects.TryGetValue(brought, out var project) ? project.Component : null)
            .Where(static component => component is not null)
            .Select(static component => component!)
            .ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, Component> ReadComponents()
    {
        var components = new Dictionary<string, Component>(StringComparer.Ordinal);

        foreach (var property in XDocument.Load(Path.Combine(Scripts.RepoRoot, "Versions.props"))
                     .Descendants()
                     .Where(static node => node.Name.LocalName.EndsWith(VersionSuffix, StringComparison.Ordinal))
                     .Where(static node => node.Parent?.Name.LocalName == "PropertyGroup"))
        {
            string name = property.Name.LocalName[..^VersionSuffix.Length];
            components[name.ToLowerInvariant()] = new Component(name.ToLowerInvariant(), property.Name.LocalName, property.Value.Trim());
        }

        Assert.NotEmpty(components);
        return components;
    }

    private static Dictionary<string, SourceProject> ReadProjects()
    {
        var projects = new Dictionary<string, SourceProject>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(SourceDirectory, "*.csproj", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(file);
            string name = Path.GetFileNameWithoutExtension(file);

            projects[name] = new SourceProject(
                Name: name,
                File: file,
                VersionProperty: VersionPropertyOf(document),
                OutputType: OutputTypeOf(file, document, []),
                Packable: Flag(document, "IsPackable") ?? true,
                PacksAnAssembly: Flag(document, "IncludeBuildOutput") ?? true,
                PackageReferences: References(document, "PackageReference"),
                ProjectReferences: References(document, "ProjectReference")
                    .Select(static include => Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar)))
                    .ToArray());
        }

        Assert.NotEmpty(projects);
        return projects;
    }

    private static Dictionary<string, IReadOnlySet<string>> ReadBringers()
    {
        var bringers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var project in Projects.Values.Where(static project => project.Packable && project.Component is not null))
        {
            foreach (string reached in Closure(project.Name))
            {
                // Only another component counts. An assembly its own component's entry point pulls
                // in is not something an adopter of a different component ever gets for free.
                if (!Projects.TryGetValue(reached, out var target) || target.Component == project.Component) continue;

                if (!bringers.TryGetValue(reached, out var set))
                {
                    bringers[reached] = set = new HashSet<string>(StringComparer.Ordinal);
                }
                set.Add(project.Component!);
            }
        }

        return bringers.ToDictionary(static entry => entry.Key, static entry => (IReadOnlySet<string>)entry.Value, StringComparer.Ordinal);
    }

    /// <summary>The property a project takes its version from, as <c>&lt;Version&gt;$(Key Version)&lt;/Version&gt;</c>.</summary>
    private static string? VersionPropertyOf(XDocument document)
    {
        string? declared = document.Descendants()
            .FirstOrDefault(static node => node.Name.LocalName == "Version" && node.Parent?.Name.LocalName == "PropertyGroup")
            ?.Value.Trim();

        return declared is not null && declared.StartsWith("$(", StringComparison.Ordinal) && declared.EndsWith(')')
            ? declared[2..^1]
            : null;
    }

    /// <summary>
    /// The output kind a project builds, following the files it imports. The kit's WinUI application
    /// block is where an application in this repository gets <c>WinExe</c> from, so a project's own
    /// file says nothing about whether it is a library and the import has to be followed to find out.
    /// Null is the SDK default, which is a library.
    /// </summary>
    private static string? OutputTypeOf(string file, XDocument document, HashSet<string> seen)
    {
        if (!seen.Add(file)) return null;

        string? declared = document.Descendants()
            .FirstOrDefault(static node => node.Name.LocalName == "OutputType")
            ?.Value.Trim();

        if (!string.IsNullOrEmpty(declared)) return declared;

        foreach (var import in document.Descendants().Where(static node => node.Name.LocalName == "Import"))
        {
            string? include = import.Attribute("Project")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;

            string? imported = ResolveImport(file, include);
            if (imported is null)
            {
                Unresolvable.Add($"{Path.GetFileNameWithoutExtension(file)} imports {include}");
                continue;
            }

            string? inherited = OutputTypeOf(imported, XDocument.Load(imported), seen);
            if (inherited is not null) return inherited;
        }

        return null;
    }

    /// <summary>An import's path, with the two properties this repository writes them with expanded.</summary>
    private static string? ResolveImport(string importingFile, string include)
    {
        string directory = Path.GetDirectoryName(importingFile)!;

        string path = include
            .Replace("$(ZeroZeroBuildDir)", BuildKitDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            .Replace("$(MSBuildThisFileDirectory)", directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            .Replace('\\', Path.DirectorySeparatorChar);

        if (path.Contains("$(", StringComparison.Ordinal)) return null;

        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(directory, path));
        return File.Exists(full) ? full : null;
    }

    private static bool? Flag(XDocument document, string name)
    {
        string? value = document.Descendants().FirstOrDefault(node => node.Name.LocalName == name)?.Value.Trim();
        return value is null ? null : string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] References(XDocument document, string item) =>
        document.Descendants()
            .Where(node => node.Name.LocalName == item)
            .Select(static node => node.Attribute("Include")?.Value)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => include!)
            .ToArray();
}

/// <summary>One component: the name its tags and notes folder use, its property, and its version.</summary>
internal sealed record Component(string Name, string Property, string Version);

internal sealed record SourceProject(
    string Name,
    string File,
    string? VersionProperty,
    string? OutputType,
    bool Packable,
    bool PacksAnAssembly,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> ProjectReferences)
{
    /// <summary>The component this project belongs to, from the version property it takes.</summary>
    public string? Component =>
        VersionProperty is not null && VersionProperty.EndsWith("Version", StringComparison.Ordinal)
            ? VersionProperty[..^"Version".Length].ToLowerInvariant()
            : null;

    /// <summary>Whether the project builds a library, which is the SDK default and anything but an
    /// executable.</summary>
    public bool IsLibrary =>
        OutputType is null || OutputType.Equals("Library", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the project takes a NuGet package, by the id NuGet matches it on.</summary>
    public bool Takes(string package) =>
        PackageReferences.Contains(package, StringComparer.OrdinalIgnoreCase);
}
