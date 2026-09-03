using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ZeroZero.Update.Tests;

/// <summary>Files for the verifier, made once per test class: a copy of the assembly under test
/// signed by a certificate made here, the same file signed by a stranger, the same file signed by
/// an impostor spelling the expected name with a key of its own, and the unsigned, tampered and
/// truncated forms. Signing goes through PowerShell's Set-AuthenticodeSignature — the primitive
/// the build kit's own signing script uses — with no timestamp, so nothing leaves the machine.
/// Every certificate is self-signed, so Windows reports its chain as untrusted, which is what the
/// studio certificate looks like on a machine that has not installed it.</summary>
public sealed class SignedFileFactory : IDisposable
{
    public const string ExpectedSubject = "CN=ZeroZero Test Signer, O=ZeroZero Software Tests, C=NO";
    public const string OtherSubject = "CN=Somebody Else, O=Elsewhere, C=NO";

    private const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";

    private const string SignScript = """
        param([string]$PfxPath, [string]$FilePath)
        $ErrorActionPreference = 'Stop'
        $password = ConvertTo-SecureString $env:ZEROZERO_TEST_PFX_PASSWORD -AsPlainText -Force
        $certificate = Get-PfxCertificate -FilePath $PfxPath -Password $password
        $signature = Set-AuthenticodeSignature -FilePath $FilePath -Certificate $certificate -HashAlgorithm SHA256
        if ($null -eq $signature.SignerCertificate) { Write-Output "no signature applied: $($signature.Status) $($signature.StatusMessage)"; exit 1 }
        if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { Write-Output "signed by another certificate"; exit 2 }
        exit 0
        """;

    public SignedFileFactory()
    {
        Root = Path.Combine(Path.GetTempPath(), "ZeroZero.Update.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        Expected = NewCodeSigningCertificate(ExpectedSubject);
        Other = NewCodeSigningCertificate(OtherSubject);
        Impostor = NewCodeSigningCertificate(ExpectedSubject);

        string source = typeof(InstallerVerifier).Assembly.Location;
        UnsignedPath = Copy(source, "unsigned.dll");
        SignedByExpectedPath = Sign(Copy(source, "expected.dll"), Expected);
        SignedByOtherPath = Sign(Copy(source, "other.dll"), Other);
        SignedByImpostorPath = Sign(Copy(source, "impostor.dll"), Impostor);
        TamperedPath = Tamper(SignedByExpectedPath, "tampered.dll");
        TruncatedPath = Truncate(SignedByExpectedPath, "truncated.dll", 4096);
    }

    public string Root { get; }

    public X509Certificate2 Expected { get; }
    public X509Certificate2 Other { get; }
    public X509Certificate2 Impostor { get; }

    public string UnsignedPath { get; }
    public string SignedByExpectedPath { get; }
    public string SignedByOtherPath { get; }
    public string SignedByImpostorPath { get; }
    public string TamperedPath { get; }
    public string TruncatedPath { get; }

    public string ExpectedThumbprintSha256 => Expected.GetCertHashString(HashAlgorithmName.SHA256);
    public string ExpectedThumbprintSha1 => Expected.Thumbprint;

    /// <summary>The expected subject with the expected certificate pinned by its SHA-256.</summary>
    public ExpectedSigner Signer => new(ExpectedSubject, [ExpectedThumbprintSha256]);

    public ExpectedSigner SignerBySha1 => new(ExpectedSubject, [ExpectedThumbprintSha1]);

    /// <summary>The expected subject and no pin — the check a subject-only design would make.</summary>
    public ExpectedSigner SignerUnpinned => new(ExpectedSubject);

    public byte[] Bytes(string path) => File.ReadAllBytes(path);

    public string Sha256(string path) => InstallerVerifier.Sha256Of(path);

    private string Copy(string source, string name)
    {
        string path = Path.Combine(Root, name);
        File.Copy(source, path);
        return path;
    }

    private string Tamper(string source, string name)
    {
        byte[] bytes = File.ReadAllBytes(source);
        // A byte in the middle of the image: inside a section the signature covers, and nowhere
        // near the signature itself or the checksum field it excludes.
        bytes[bytes.Length / 2] ^= 0xFF;
        string path = Path.Combine(Root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Truncate(string source, string name, int bytesCut)
    {
        byte[] bytes = File.ReadAllBytes(source);
        string path = Path.Combine(Root, name);
        File.WriteAllBytes(path, bytes[..(bytes.Length - bytesCut)]);
        return path;
    }

    private static X509Certificate2 NewCodeSigningCertificate(string subject)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(CodeSigningOid)], true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 ephemeral = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    private string Sign(string path, X509Certificate2 certificate)
    {
        // A throwaway secret for a file that lives for the length of one call.
        string password = Guid.NewGuid().ToString("N");
        string pfx = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".pfx");
        File.WriteAllBytes(pfx, certificate.Export(X509ContentType.Pfx, password));

        string script = Path.Combine(Root, "sign.ps1");
        if (!File.Exists(script)) File.WriteAllText(script, SignScript);

        var start = new ProcessStartInfo("pwsh")
        {
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" -PfxPath \"{pfx}\" -FilePath \"{path}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["ZEROZERO_TEST_PFX_PASSWORD"] = password;

        try
        {
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Signing {path} failed with exit code {process.ExitCode}: {output} {errors}");
        }
        finally
        {
            File.Delete(pfx);
        }

        return path;
    }

    public void Dispose()
    {
        Expected.Dispose();
        Other.Dispose();
        Impostor.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temporary folder's own housekeeping.
        }
    }
}
