using Xunit;
using System.Text.Json.Nodes;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>The record pack-component.ps1 writes beside the packages: the build's own statement of what it produced.</summary>
[Collection(PackedReleaseCollection.Name)]
public sealed class PackComponentTests(PackedRelease release)
{
    [Fact]
    public void Records_the_hash_of_the_package_as_packed()
    {
        var record = JsonNode.Parse(File.ReadAllText(release.RecordPath))!;

        Assert.Equal(release.Tag, record["tag"]!.GetValue<string>());
        Assert.Equal(PackedRelease.Key, record["key"]!.GetValue<string>());
        Assert.Equal(release.Version, record["version"]!.GetValue<string>());
        Assert.Equal(release.Commit, record["commit"]!.GetValue<string>());
        var artefact = Assert.Single(record["artefacts"]!.AsArray());
        Assert.Equal(release.PackageName, artefact!["name"]!.GetValue<string>());
        Assert.Equal(PackedRelease.PackageId, artefact["id"]!.GetValue<string>());
        Assert.Equal(release.Version, artefact["version"]!.GetValue<string>());
        Assert.Equal(PackedRelease.Sha256(release.PackagePath), artefact["sha256"]!.GetValue<string>());
    }

    [Fact]
    public void Says_where_the_record_landed()
    {
        Assert.Contains($"Recorded their hashes at commit {release.Commit}", release.PackResult.Output);
    }

    [Fact]
    public void Refuses_an_output_folder_that_already_holds_a_package()
    {
        var result = release.Pack(release.BuildDirectory);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("already holds", result.Output);
    }
}
