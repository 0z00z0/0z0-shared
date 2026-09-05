using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>The per-component release-notes folders against the components the build declares.</summary>
/// <remarks>
/// <para>A release publishes the note for the tag it is building, so a component with no folder, or
/// with no note at the version it declares, releases with nothing to say. A folder for a component
/// nothing declares is worse: it reads as a component that exists and has notes, and no tag will
/// ever be cut for it.</para>
/// <para>The loose notes at the root of the folder are from the era of one shared number for the
/// whole family and belong to no component, so only directories are compared.</para>
/// </remarks>
public sealed class ReleaseNotesTests
{
    private static readonly string Notes = Path.Combine(Scripts.RepoRoot, "docs", "release-notes");

    [Fact]
    public void Every_component_the_versions_file_declares_has_a_notes_folder()
    {
        var folders = Folders();
        var missing = Repository.Components.Keys.Where(key => !folders.Contains(key)).Order().ToArray();

        Assert.True(
            missing.Length == 0,
            $"docs/release-notes has no folder for {string.Join(", ", missing)}, which Versions.props declares, so a release of it would publish no notes.");
    }

    [Fact]
    public void There_is_no_notes_folder_for_a_component_the_versions_file_does_not_declare()
    {
        var stale = Folders().Where(folder => !Repository.Components.ContainsKey(folder)).Order().ToArray();

        Assert.True(
            stale.Length == 0,
            $"docs/release-notes holds a folder for {string.Join(", ", stale)}, which Versions.props does not declare, so no tag can ever publish it.");
    }

    [Fact]
    public void Every_component_has_a_note_for_the_version_it_declares()
    {
        var missing = Repository.Components.Values
            .Where(static component => !File.Exists(Path.Combine(Notes, component.Name, $"v{component.Version}.md")))
            .Select(static component => $"{component.Name} at {component.Version}")
            .Order()
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"docs/release-notes has no note for {string.Join(", ", missing)}, and a release publishes the note for the version it builds.");
    }

    private static HashSet<string> Folders()
    {
        var folders = Directory.EnumerateDirectories(Notes)
            .Select(static folder => Path.GetFileName(folder))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(folders);
        return folders;
    }
}
