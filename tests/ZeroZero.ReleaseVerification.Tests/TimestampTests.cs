using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>
/// The timestamp assertions, in the signing script and again at release time. A signing run that
/// reaches no timestamp authority produces a correctly signed file carrying no timestamp: the same
/// status, the same signer and the same empty unsigned-attribute set as one signed with the
/// timestamp deliberately declined. Nothing but the countersignature separates them, and an
/// untimestamped signature stops verifying the day the certificate expires.
/// </summary>
/// <remarks>
/// Offline throughout. The certificate is made here and lives in a PFX file for the length of the
/// class; no certificate store is touched, so nothing on the machine trusts it and every signature
/// reads back as an untrusted root, which is what the studio certificate looks like on a fresh
/// runner. The unreachable authority is the loopback address on a port nothing listens on, which
/// refuses at once rather than waiting out a timeout.
/// </remarks>
public sealed class TimestampTests : IDisposable
{
    private const string UnreachableAuthority = "http://127.0.0.1:1/";
    private const string Subject = "CN=ZeroZero Timestamp Tests, O=ZeroZero Software Tests, C=NO";
    private const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";
    private const string Tag = "app-v1.0.0";

    private static readonly string Commit = new('b', 40);

    private static readonly string SigningScript =
        Path.Combine(Scripts.RepoRoot, "src", "ZeroZero.Build", "scripts", "Sign-Executable.ps1");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "zz-timestamp-" + Guid.NewGuid().ToString("n"));
    private readonly string _password = Guid.NewGuid().ToString("n");
    private readonly X509Certificate2 _certificate;
    private readonly string _pfx;

    public TimestampTests()
    {
        Directory.CreateDirectory(_root);
        _certificate = NewCodeSigningCertificate();
        _pfx = Path.Combine(_root, "signing.pfx");
        File.WriteAllBytes(_pfx, _certificate.Export(X509ContentType.Pfx, _password));
    }

    private IReadOnlyDictionary<string, string?> Environment =>
        new Dictionary<string, string?> { ["ZEROZERO_SIGN_PFX_PASSWORD"] = _password };

    [Fact]
    public void The_signing_script_refuses_a_file_it_could_not_timestamp()
    {
        string file = Copy("unreachable.dll");

        var result = Sign(file, "-TimestampServer", UnreachableAuthority);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("ZZS013", result.Output, StringComparison.Ordinal);
        Assert.Contains("carries no timestamp", result.Output, StringComparison.Ordinal);
        // The file it refused really is signed and really carries no timestamp, so the guard is
        // reading the case it names rather than failing for some other reason.
        Assert.Equal(Subject, SignerOf(file));
        Assert.Null(TimestamperOf(file));
    }

    [Fact]
    public void The_signing_script_signs_without_a_timestamp_when_one_is_declined()
    {
        string file = Copy("declined.dll");

        var result = Sign(file, "-NoTimestamp");

        Assert.True(result.Passed, result.ToString());
        Assert.Contains("no timestamp was asked for", result.Output, StringComparison.Ordinal);
        Assert.Equal(Subject, SignerOf(file));
    }

    [Fact]
    public void Release_verification_refuses_a_published_executable_with_no_timestamp()
    {
        // Signed the one way this repository can sign offline, which is the shape the defect ships:
        // a valid signature by the right certificate, and no countersignature behind it.
        string file = Copy("published.dll");
        var signed = Sign(file, "-NoTimestamp");
        Assert.True(signed.Passed, signed.ToString());
        var (feed, record) = Publish(file, "published.dll");

        var result = Scripts.Run("verify-release.ps1", null,
            "-Tag", Tag, "-Artefacts", record, "-Commit", Commit, "-Location", feed, "-Signer", Subject);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("carries no timestamp", result.Output, StringComparison.Ordinal);
    }

    private ScriptResult Sign(string file, params string[] more)
    {
        var arguments = new List<string> { "-Path", file, "-PfxPath", _pfx };
        arguments.AddRange(more);
        return Scripts.RunPath(SigningScript, Environment, arguments.ToArray());
    }

    private string Copy(string name)
    {
        // This assembly: a plain unsigned image, and not one the operating system catalogues, so
        // the signature read back is the one just applied.
        string path = Path.Combine(_root, name);
        File.Copy(typeof(TimestampTests).Assembly.Location, path);
        return path;
    }

    private (string Feed, string Record) Publish(string file, string name)
    {
        string feed = Path.Combine(_root, "feed");
        Directory.CreateDirectory(feed);
        File.Copy(file, Path.Combine(feed, name));
        string record = Path.Combine(_root, "release-artefacts.json");
        File.WriteAllText(record, JsonSerializer.Serialize(new
        {
            tag = Tag,
            version = "1.0.0",
            commit = Commit,
            artefacts = new[] { new { name, sha256 = PackedRelease.Sha256(file) } },
        }));
        return (feed, record);
    }

    private static string SignerOf(string file)
    {
        var result = Scripts.Command($"(Get-AuthenticodeSignature -LiteralPath {Scripts.Quote(file)}).SignerCertificate.Subject");
        Assert.True(result.Passed, result.ToString());
        return result.Output.Trim();
    }

    private static string? TimestamperOf(string file)
    {
        var result = Scripts.Command($"(Get-AuthenticodeSignature -LiteralPath {Scripts.Quote(file)}).TimeStamperCertificate.Subject");
        Assert.True(result.Passed, result.ToString());
        string subject = result.Output.Trim();
        return subject.Length == 0 ? null : subject;
    }

    private static X509Certificate2 NewCodeSigningCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(Subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(CodeSigningOid)], true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 ephemeral = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    public void Dispose()
    {
        _certificate.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
