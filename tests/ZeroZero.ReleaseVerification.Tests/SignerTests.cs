using Xunit;
using System.Text.Json;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// The -Signer assertion on a fetched executable. Nothing this repository packs is signed, so the
/// signed case is the pwsh executable on the machine, which Microsoft signs, and the unsigned case
/// is this test assembly. The record for them is written here: it lists a real file with its real
/// hash, and the assertion under test is the signature, not the bytes.
/// </summary>
public sealed class SignerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "zz-signer-" + Guid.NewGuid().ToString("n"));
    private const string Tag = "app-v1.0.0";
    private static readonly string Commit = new('a', 40);

    public SignerTests() => Directory.CreateDirectory(root);

    private static string Pwsh { get; } = FindPwsh();

    private static string SubjectOf(string executable)
    {
        var result = Scripts.Command($"(Get-AuthenticodeSignature -LiteralPath {Scripts.Quote(executable)}).SignerCertificate.Subject");
        Assert.True(result.Passed, result.ToString());
        return result.Output.Trim();
    }

    private (string Feed, string Record) Publish(string file, string name)
    {
        var feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(feed);
        File.Copy(file, Path.Combine(feed, name));
        var record = Path.Combine(root, "release-artefacts.json");
        File.WriteAllText(record, JsonSerializer.Serialize(new
        {
            tag = Tag,
            version = "1.0.0",
            commit = Commit,
            artefacts = new[] { new { name, sha256 = PackedRelease.Sha256(file) } },
        }));
        return (feed, record);
    }

    private static string ThumbprintOf(string executable)
    {
        var result = Scripts.Command($"(Get-AuthenticodeSignature -LiteralPath {Scripts.Quote(executable)}).SignerCertificate.Thumbprint");
        Assert.True(result.Passed, result.ToString());
        return result.Output.Trim();
    }

    private static ScriptResult Verify(string feed, string record, string signer, params string[] more)
    {
        var arguments = new List<string> { "-Tag", Tag, "-Artefacts", record, "-Commit", Commit, "-Location", feed, "-Signer", signer };
        arguments.AddRange(more);
        return Scripts.Run("verify-release.ps1", null, arguments.ToArray());
    }

    [Fact]
    public void Accepts_an_executable_signed_by_the_expected_subject()
    {
        var subject = SubjectOf(Pwsh);
        Assert.False(string.IsNullOrWhiteSpace(subject), "pwsh.exe on this machine is not signed, so the signed case cannot be exercised here");
        var (feed, record) = Publish(Pwsh, "pwsh.exe");

        var result = Verify(feed, record, subject);

        Assert.True(result.Passed, result.ToString());
        Assert.Contains($"signed by '{subject}'", result.Output);
    }

    [Fact]
    public void Accepts_the_certificate_with_the_expected_thumbprint()
    {
        var subject = SubjectOf(Pwsh);
        var thumbprint = ThumbprintOf(Pwsh);
        Assert.Matches("^[0-9A-Fa-f]{40}$", thumbprint);
        var (feed, record) = Publish(Pwsh, "pwsh.exe");

        var result = Verify(feed, record, subject, "-SignerThumbprint", thumbprint);

        Assert.True(result.Passed, result.ToString());
        Assert.Contains($"thumbprint {thumbprint}", result.Output);
    }

    [Fact]
    public void Refuses_the_right_subject_on_another_certificate()
    {
        // A subject is a string anyone can put on a self-signed certificate; the thumbprint is
        // the certificate itself.
        var subject = SubjectOf(Pwsh);
        var (feed, record) = Publish(Pwsh, "pwsh.exe");

        var result = Verify(feed, record, subject, "-SignerThumbprint", new string('A', 40));

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("the certificate is not the release's", result.Output);
    }

    [Fact]
    public void Refuses_an_executable_signed_by_another_subject()
    {
        var (feed, record) = Publish(Pwsh, "pwsh.exe");

        var result = Verify(feed, record, "CN=Someone Else");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("not by 'CN=Someone Else'", result.Output);
    }

    [Fact]
    public void Refuses_a_signature_that_no_longer_matches_the_file()
    {
        // One byte changed well inside the image: the certificate is still attached and still
        // names the right subject, and the signed hash no longer describes the file.
        var subject = SubjectOf(Pwsh);
        var tampered = Path.Combine(root, "pwsh.exe");
        var bytes = File.ReadAllBytes(Pwsh);
        bytes[0x2000] ^= 0xFF;
        File.WriteAllBytes(tampered, bytes);
        var (feed, record) = Publish(tampered, "pwsh.exe");

        var result = Verify(feed, record, subject);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("verifies as HashMismatch", result.Output);
    }

    [Fact]
    public void Refuses_an_unsigned_executable()
    {
        var unsigned = typeof(SignerTests).Assembly.Location;
        var (feed, record) = Publish(unsigned, Path.GetFileName(unsigned));

        var result = Verify(feed, record, "CN=Someone Else");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("is not signed", result.Output);
    }

    private static string FindPwsh()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory.Trim(), "pwsh.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("pwsh.exe is not on PATH.");
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }
}
