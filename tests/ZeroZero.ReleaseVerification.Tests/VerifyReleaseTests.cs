using Xunit;
using System.Text;
using System.Text.Json.Nodes;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// verify-release.ps1 against artefacts the repository's own packing produced. The positive case
/// fetches the published bytes and passes; every other case is a well-formed artefact or record
/// that describes something other than this build, and must be refused naming why.
/// </summary>
[Collection(PackedReleaseCollection.Name)]
public sealed class VerifyReleaseTests(PackedRelease release)
{
    // Three names rather than overloads: with params, a string first argument binds to the record.
    private ScriptResult Verify(params string[] arguments) => VerifyRecord(release.RecordPath, arguments);

    private ScriptResult VerifyRecord(string record, params string[] arguments) =>
        VerifyAs(release.Tag, record, release.Commit, arguments);

    private static ScriptResult VerifyAs(string tag, string record, string? commit, params string[] arguments)
    {
        var all = new List<string> { "-Tag", tag, "-Artefacts", record };
        if (commit is not null)
        {
            all.Add("-Commit");
            all.Add(commit);
        }
        all.AddRange(arguments);
        return Scripts.Run("verify-release.ps1", null, all.ToArray());
    }

    [Fact]
    public void Accepts_the_published_bytes_when_they_are_the_builds()
    {
        var result = Verify("-Location", release.Feed);

        Assert.True(result.Passed, result.ToString());
        Assert.Contains("every artefact fetched is the build's own", result.Output);
        Assert.Contains($"nuspec: {PackedRelease.PackageId} {release.Version} at {release.Commit}", result.Output);
        Assert.Contains($"ZeroZero.Primitives.dll: {release.Version}+", result.Output);
    }

    [Fact]
    public void Accepts_the_nuget_layout()
    {
        var feed = release.NewDirectory("nuget-feed");
        var folder = Path.Combine(feed, "zerozero.primitives", release.Version);
        Directory.CreateDirectory(folder);
        File.Copy(release.PackagePath, Path.Combine(folder, $"zerozero.primitives.{release.Version}.nupkg"));

        var result = Verify("-Location", feed, "-Layout", "NuGet");

        Assert.True(result.Passed, result.ToString());
    }

    [Fact]
    public void Refuses_a_published_package_that_is_another_pack_of_this_build()
    {
        // The measured failure, reproduced: what is published is a well-formed package at the right
        // id, version and commit whose bytes are not what this build produced. Every shape check
        // passes on it, and the output shows them passing.
        var other = release.NewDirectory("other-pack");
        var packed = release.Pack(other);
        Assert.True(packed.Passed, packed.ToString());
        var otherPackage = Path.Combine(other, release.PackageName);
        Assert.NotEqual(PackedRelease.Sha256(release.PackagePath), PackedRelease.Sha256(otherPackage));
        var feed = release.NewDirectory("stale-feed");
        PackedRelease.Publish(otherPackage, feed);

        var result = Verify("-Location", feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("What is published is not this build", result.Output);
        Assert.Contains($"nuspec: {PackedRelease.PackageId} {release.Version} at {release.Commit}", result.Output);
        Assert.Contains($"ZeroZero.Primitives.dll: {release.Version}+", result.Output);
    }

    [Fact]
    public void Refuses_a_location_where_nothing_was_published()
    {
        var empty = release.NewDirectory("empty-feed");

        var result = Verify("-Location", empty);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("nothing was published there", result.Output);
    }

    [Fact]
    public void Refuses_a_record_of_another_tag()
    {
        var record = Artefacts.Record(release.RecordPath, Path.Combine(release.NewDirectory("record"), "release-artefacts.json"),
            edit: node => node["tag"] = $"config-v{release.Version}");

        var result = VerifyRecord(record, "-Location",release.Feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("The record is another release's", result.Output);
    }

    [Fact]
    public void Refuses_a_tag_that_does_not_end_in_the_recorded_version()
    {
        var tag = $"{PackedRelease.Key}-v9.9.9";
        var record = Artefacts.Record(release.RecordPath, Path.Combine(release.NewDirectory("record"), "release-artefacts.json"),
            edit: node => node["tag"] = tag);

        var result = VerifyAs(tag, record, release.Commit, "-Location", release.Feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains($"does not end in v{release.Version}", result.Output);
    }

    [Fact]
    public void Refuses_a_record_of_another_commit()
    {
        var result = VerifyAs(release.Tag, release.RecordPath,new string('0', 40), "-Location", release.Feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("The record is another build's", result.Output);
    }

    [Fact]
    public void Requires_a_commit()
    {
        var result = VerifyAs(release.Tag, release.RecordPath,commit: null, "-Location", release.Feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("No commit given", result.Output);
    }

    [Fact]
    public void Refuses_an_empty_record()
    {
        var record = Artefacts.Record(release.RecordPath, Path.Combine(release.NewDirectory("record"), "release-artefacts.json"),
            edit: node => node["artefacts"] = new JsonArray());

        var result = VerifyRecord(record, "-Location",release.Feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("lists no artefacts", result.Output);
    }

    [Fact]
    public void Refuses_a_missing_record()
    {
        var result = VerifyRecord(Path.Combine(release.NewDirectory("record"), "release-artefacts.json"), "-Location", release.Feed);

        // The verifier's own wording, not the file system's: a read that throws also says "does
        // not exist", and that phrase alone could not tell the guard from an incidental error.
        Assert.False(result.Passed, result.ToString());
        Assert.Contains("pack-component.ps1 writes it beside the packages", result.Output);
    }

    [Fact]
    public void Refuses_nothing_to_verify_against()
    {
        var result = Verify();

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("Nothing to verify against", result.Output);
    }

    [Fact]
    public void Refuses_a_package_whose_nuspec_names_another_commit()
    {
        // Bytes agree with the record, the nuspec is valid, and it says the package is another
        // build's: the case where a consistent artefact describes the wrong thing.
        var feed = release.NewDirectory("feed");
        var package = Artefacts.CopyWithEntryText(release.PackagePath, Path.Combine(feed, release.PackageName),
            $"{PackedRelease.PackageId}.nuspec", text => text.Replace($"commit=\"{release.Commit}\"", $"commit=\"{new string('0', 40)}\""));
        var record = Artefacts.Record(release.RecordPath, Path.Combine(feed, "release-artefacts.json"), hashOfPackage: package);

        var result = VerifyRecord(record, "-Location",feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("The package describes another build", result.Output);
        Assert.DoesNotContain("What is published is not this build", result.Output);
    }

    [Fact]
    public void Refuses_a_package_whose_nuspec_names_no_commit()
    {
        var feed = release.NewDirectory("feed");
        var package = Artefacts.CopyWithEntryText(release.PackagePath, Path.Combine(feed, release.PackageName),
            $"{PackedRelease.PackageId}.nuspec", text => text.Replace($" commit=\"{release.Commit}\"", ""));
        var record = Artefacts.Record(release.RecordPath, Path.Combine(feed, "release-artefacts.json"), hashOfPackage: package);

        var result = VerifyRecord(record, "-Location",feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("names no repository commit", result.Output);
    }

    [Fact]
    public void Refuses_a_package_whose_nuspec_declares_another_version()
    {
        var feed = release.NewDirectory("feed");
        var package = Artefacts.CopyWithEntryText(release.PackagePath, Path.Combine(feed, release.PackageName),
            $"{PackedRelease.PackageId}.nuspec", text => text.Replace($"<version>{release.Version}</version>", "<version>9.9.9</version>"));
        var record = Artefacts.Record(release.RecordPath, Path.Combine(feed, "release-artefacts.json"), hashOfPackage: package);

        var result = VerifyRecord(record, "-Location",feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains($"says version '9.9.9'; the release is {release.Version}", result.Output);
    }

    [Fact]
    public void Refuses_a_package_whose_nuspec_declares_another_id()
    {
        var feed = release.NewDirectory("feed");
        var package = Artefacts.CopyWithEntryText(release.PackagePath, Path.Combine(feed, release.PackageName),
            $"{PackedRelease.PackageId}.nuspec", text => text.Replace($"<id>{PackedRelease.PackageId}</id>", "<id>ZeroZero.Other</id>"));
        var record = Artefacts.Record(release.RecordPath, Path.Combine(feed, "release-artefacts.json"), hashOfPackage: package);

        var result = VerifyRecord(record, "-Location",feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains($"says id 'ZeroZero.Other'; the artefact is {PackedRelease.PackageId}", result.Output);
    }

    [Fact]
    public void Refuses_a_package_whose_assembly_was_built_at_another_commit()
    {
        // The stale-build case: a nuspec written at pack time says this commit while the assembly
        // inside was built at another. The stamp lives in the version resource as UTF-16.
        var feed = release.NewDirectory("feed");
        var stamp = Encoding.Unicode.GetBytes("+" + release.Commit[..7]);
        var other = Encoding.Unicode.GetBytes("+0000000");
        var occurrences = 0;
        var package = Artefacts.CopyWithEntryBytes(release.PackagePath, Path.Combine(feed, release.PackageName),
            "lib/net10.0/ZeroZero.Primitives.dll", bytes => Artefacts.ReplaceAll(bytes, stamp, other, out occurrences));
        Assert.True(occurrences > 0, "the assembly carries no UTF-16 stamp to rewrite");
        var record = Artefacts.Record(release.RecordPath, Path.Combine(feed, "release-artefacts.json"), hashOfPackage: package);

        var result = VerifyRecord(record, "-Location",feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("was built at commit 0000000", result.Output);
        Assert.Contains("packed from a stale build", result.Output);
    }

    [Fact]
    public void Refuses_a_package_whose_assembly_reports_another_version()
    {
        // The nuspec says the released version; the assembly inside was built as another.
        var feed = release.NewDirectory("feed");
        var other = new string('9', release.Version.Length);
        var stamp = Encoding.Unicode.GetBytes(release.Version + "+");
        var replaced = Encoding.Unicode.GetBytes(other + "+");
        var occurrences = 0;
        var package = Artefacts.CopyWithEntryBytes(release.PackagePath, Path.Combine(feed, release.PackageName),
            "lib/net10.0/ZeroZero.Primitives.dll", bytes => Artefacts.ReplaceAll(bytes, stamp, replaced, out occurrences));
        Assert.True(occurrences > 0, "the assembly carries no UTF-16 stamp to rewrite");
        var record = Artefacts.Record(release.RecordPath, Path.Combine(feed, "release-artefacts.json"), hashOfPackage: package);

        var result = VerifyRecord(record, "-Location", feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains($"reports version {other}; the release is {release.Version}", result.Output);
    }

    [Fact]
    public void Refuses_a_package_whose_assembly_carries_no_stamp()
    {
        // An assembly built where git was not available reports the bare number. It cannot say
        // which build it is, and a package holding it is refused rather than passed unread.
        var feed = release.NewDirectory("feed");
        var stamp = Encoding.Unicode.GetBytes("+" + release.Commit[..7]);
        var none = Encoding.Unicode.GetBytes("-unknown");
        var occurrences = 0;
        var package = Artefacts.CopyWithEntryBytes(release.PackagePath, Path.Combine(feed, release.PackageName),
            "lib/net10.0/ZeroZero.Primitives.dll", bytes => Artefacts.ReplaceAll(bytes, stamp, none, out occurrences));
        Assert.True(occurrences > 0, "the assembly carries no UTF-16 stamp to rewrite");
        var record = Artefacts.Record(release.RecordPath, Path.Combine(feed, "release-artefacts.json"), hashOfPackage: package);

        var result = VerifyRecord(record, "-Location", feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("cannot say which build it is", result.Output);
    }

    [Fact]
    public void Refuses_a_package_without_a_nuspec()
    {
        var feed = release.NewDirectory("feed");
        var package = Artefacts.CopyWithoutEntry(release.PackagePath, Path.Combine(feed, release.PackageName), $"{PackedRelease.PackageId}.nuspec");
        var record = Artefacts.Record(release.RecordPath, Path.Combine(feed, "release-artefacts.json"), hashOfPackage: package);

        var result = VerifyRecord(record, "-Location", feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("holds 0 nuspec files", result.Output);
    }

    [Fact]
    public void Refuses_a_package_whose_record_entry_has_no_id()
    {
        var record = Artefacts.Record(release.RecordPath, Path.Combine(release.NewDirectory("record"), "release-artefacts.json"),
            edit: node => node["artefacts"]![0]!.AsObject().Remove("id"));

        var result = VerifyRecord(record, "-Location", release.Feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("gives it no id or version", result.Output);
    }

    [Fact]
    public void Refuses_the_nuget_layout_for_an_entry_without_an_id()
    {
        var record = Artefacts.Record(release.RecordPath, Path.Combine(release.NewDirectory("record"), "release-artefacts.json"),
            edit: node => node["artefacts"]![0]!.AsObject().Remove("id"));

        var result = VerifyRecord(record, "-Location", release.Feed, "-Layout", "NuGet");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("the NuGet layout needs both", result.Output);
    }

    [Fact]
    public void Refuses_a_record_whose_hash_is_not_a_sha256()
    {
        // The record itself with a placeholder where the hash should be: nothing downstream may
        // treat that as a hash to compare against.
        var record = Artefacts.Record(release.RecordPath, Path.Combine(release.NewDirectory("record"), "release-artefacts.json"),
            edit: node => node["artefacts"]![0]!["sha256"] = "{InstallerSha256}");

        var result = VerifyRecord(record, "-Location", release.Feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("has no SHA-256", result.Output);
    }

    [Fact]
    public void Refuses_a_record_entry_with_no_name()
    {
        var record = Artefacts.Record(release.RecordPath, Path.Combine(release.NewDirectory("record"), "release-artefacts.json"),
            edit: node => node["artefacts"]![0]!.AsObject().Remove("name"));

        var result = VerifyRecord(record, "-Location", release.Feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("has no name", result.Output);
    }

    [Fact]
    public void Refuses_a_record_with_no_commit()
    {
        var record = Artefacts.Record(release.RecordPath, Path.Combine(release.NewDirectory("record"), "release-artefacts.json"),
            edit: node => node.Remove("commit"));

        var result = VerifyRecord(record, "-Location", release.Feed);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("has no 'commit'", result.Output);
    }

    [Fact]
    public void Refuses_a_signing_step_that_was_skipped_when_signing_is_required()
    {
        var result = Verify("-Location", release.Feed, "-RequireSigned", "-SigningOutcome", "skipped");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("Skipped means the step never ran", result.Output);
    }

    [Fact]
    public void Requires_a_signing_outcome_when_signing_is_required()
    {
        var result = Verify("-Location", release.Feed, "-RequireSigned");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("no signing outcome was given", result.Output);
    }

    [Fact]
    public void Refuses_a_failed_signing_step_even_when_signing_is_optional()
    {
        var result = Verify("-Location", release.Feed, "-SigningOutcome", "failure");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("did not succeed", result.Output);
    }

    [Fact]
    public void Accepts_a_successful_signing_step()
    {
        var result = Verify("-Location", release.Feed, "-RequireSigned", "-SigningOutcome", "success");

        Assert.True(result.Passed, result.ToString());
        Assert.Contains("recorded outcome is success", result.Output);
    }

    [Fact]
    public void Accepts_a_skipped_unsigned_step_as_the_evidence_of_signing()
    {
        // The measured technique: the warn-only step that says an installer is unsigned cannot
        // fail the job, so its being skipped is the only record that the installer was signed.
        var result = Verify("-Location", release.Feed, "-RequireSigned", "-UnsignedOutcome", "skipped");

        Assert.True(result.Passed, result.ToString());
        Assert.Contains("so the installer was signed", result.Output);
    }

    [Fact]
    public void Refuses_an_unsigned_step_that_ran_when_signing_is_required()
    {
        // The step warned and the job stayed green: the release shipped unsigned.
        var result = Verify("-Location", release.Feed, "-RequireSigned", "-UnsignedOutcome", "success");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("the installer shipped unsigned", result.Output);
    }

    [Fact]
    public void Refuses_an_unsigned_step_that_did_not_complete_even_when_signing_is_optional()
    {
        var result = Verify("-Location", release.Feed, "-UnsignedOutcome", "failure");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("did not complete", result.Output);
    }

    [Fact]
    public void Refuses_when_the_two_step_outcomes_disagree()
    {
        // Each form must hold on its own: a signing step that succeeded does not excuse an
        // unsigned-installer step that ran.
        var result = Verify("-Location", release.Feed, "-RequireSigned", "-SigningOutcome", "success", "-UnsignedOutcome", "success");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("the installer shipped unsigned", result.Output);
    }

    [Fact]
    public void Writes_a_report_of_what_was_fetched()
    {
        var report = Path.Combine(release.NewDirectory("report"), "verification.json");

        var result = Verify("-Location", release.Feed, "-Report", report);

        Assert.True(result.Passed, result.ToString());
        var node = JsonNode.Parse(File.ReadAllText(report))!;
        Assert.True(node["passed"]!.GetValue<bool>());
        Assert.Equal(release.Tag, node["tag"]!.GetValue<string>());
        var artefact = node["artefacts"]![0]!;
        Assert.Equal(release.PackageName, artefact["name"]!.GetValue<string>());
        Assert.Equal(Artefacts.RecordedHash(release.RecordPath), artefact["sha256"]!.GetValue<string>());
        Assert.True(artefact["passed"]!.GetValue<bool>());
    }
}
