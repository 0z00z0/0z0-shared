using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// verify-release.ps1 with a manifest: the manifest is written from a template through the
/// rewriter, the way an application's release does, and points at a real packed artefact in a
/// folder feed. The refusal that matters is the one no schema check can make: a manifest that is
/// well formed, points at a file that exists and matches that file's hash, and describes a build
/// that is not this one.
/// </summary>
[Collection(PackedReleaseCollection.Name)]
public sealed class ManifestVerificationTests(PackedRelease release)
{
    private const string Template =
        "PackageIdentifier: ZeroZero.Primitives\n" +
        "PackageVersion: {Version}\n" +
        "Installers:\n" +
        "  - Architecture: x64\n" +
        "    InstallerUrl: {InstallerUrl}\n" +
        "    InstallerSha256: {InstallerSha256}\n" +
        "ManifestType: installer\n";

    private string Manifest(string? url, string? hash, string? version)
    {
        var path = Path.Combine(release.NewDirectory("manifest"), "installer.yaml");
        File.WriteAllText(path, Template);
        foreach (var (key, value) in new[] { ("PackageVersion", version), ("InstallerUrl", url), ("InstallerSha256", hash) })
        {
            if (value is null) continue;
            var result = Scripts.Manifest($"Set-ManifestValue -Path {Scripts.Quote(path)} -Key {key} -Value {Scripts.Quote(value)}");
            Assert.True(result.Passed, result.ToString());
        }
        return path;
    }

    private ScriptResult Verify(string manifest) =>
        Scripts.Run("verify-release.ps1", null,
            "-Tag", release.Tag, "-Artefacts", release.RecordPath, "-Commit", release.Commit, "-Manifest", manifest);

    [Fact]
    public void Accepts_a_manifest_that_describes_this_build()
    {
        var manifest = Manifest(Path.Combine(release.Feed, release.PackageName), Artefacts.RecordedHash(release.RecordPath), release.Version);

        var result = Verify(manifest);

        Assert.True(result.Passed, result.ToString());
        Assert.Contains("the manifest's and the build's", result.Output);
    }

    [Fact]
    public void Refuses_a_manifest_that_describes_the_previous_release()
    {
        // The measured failure, reproduced at the manifest: the version is right, the URL is this
        // release's file name, the file is there, and the declared hash is the hash of that file.
        // Only the file is another build's. Shape, reachability and self-consistency all hold.
        var other = release.NewDirectory("previous-pack");
        var packed = release.Pack(other);
        Assert.True(packed.Passed, packed.ToString());
        var previous = Path.Combine(other, release.PackageName);
        var feed = release.NewDirectory("previous-feed");
        PackedRelease.Publish(previous, feed);
        var published = Path.Combine(feed, release.PackageName);
        var manifest = Manifest(published, PackedRelease.Sha256(published), release.Version);

        var result = Verify(manifest);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("The manifest describes a file that is not this build", result.Output);
        Assert.DoesNotContain("does not describe what it points at", result.Output);
    }

    [Fact]
    public void Refuses_a_manifest_whose_placeholders_survived()
    {
        // The frozen template: the version was rewritten and the URL and hash were not. It is
        // valid YAML with every key present.
        var manifest = Manifest(url: null, hash: null, version: release.Version);

        var result = Verify(manifest);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains($"does not contain the version {release.Version}", result.Output);
        Assert.Contains("which this build did not produce", result.Output);
    }

    [Fact]
    public void Refuses_a_manifest_whose_hash_is_a_placeholder()
    {
        var manifest = Manifest(Path.Combine(release.Feed, release.PackageName), hash: null, version: release.Version);

        var result = Verify(manifest);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("is not a SHA-256; a placeholder that was never rewritten looks like this", result.Output);
    }

    [Fact]
    public void Refuses_a_manifest_declaring_another_version()
    {
        var manifest = Manifest(Path.Combine(release.Feed, release.PackageName), Artefacts.RecordedHash(release.RecordPath), "9.9.9");

        var result = Verify(manifest);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains($"declares PackageVersion '9.9.9'; the release is {release.Version}", result.Output);
    }

    [Fact]
    public void Refuses_a_manifest_naming_a_file_this_build_did_not_produce()
    {
        var feed = release.NewDirectory("renamed-feed");
        var renamed = Path.Combine(feed, $"ZeroZero.Primitives-{release.Version}-setup.nupkg");
        File.Copy(release.PackagePath, renamed);
        var manifest = Manifest(renamed, PackedRelease.Sha256(renamed), release.Version);

        var result = Verify(manifest);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("which this build did not produce", result.Output);
    }

    [Fact]
    public void Refuses_a_manifest_whose_hash_disagrees_with_what_it_points_at()
    {
        var wrong = new string('a', 64);
        var manifest = Manifest(Path.Combine(release.Feed, release.PackageName), wrong, release.Version);

        var result = Verify(manifest);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("The manifest does not describe what it points at", result.Output);
    }

    [Fact]
    public void Refuses_a_manifest_whose_url_cannot_be_fetched()
    {
        var missing = Path.Combine(release.NewDirectory("missing-feed"), release.PackageName);
        var manifest = Manifest(missing, Artefacts.RecordedHash(release.RecordPath), release.Version);

        var result = Verify(manifest);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("nothing was published there", result.Output);
    }

    [Fact]
    public void Refuses_a_manifest_that_does_not_exist()
    {
        var path = Path.Combine(release.NewDirectory("manifest"), "installer.yaml");

        var result = Verify(path);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains($"Manifest {path} does not exist.", result.Output);
    }

    [Fact]
    public void Refuses_a_manifest_missing_a_key()
    {
        var path = Path.Combine(release.NewDirectory("manifest"), "installer.yaml");
        File.WriteAllText(path, Template.Replace("    InstallerSha256: {InstallerSha256}\n", ""));

        var result = Verify(path);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("'InstallerSha256' matches no line", result.Output);
    }
}
