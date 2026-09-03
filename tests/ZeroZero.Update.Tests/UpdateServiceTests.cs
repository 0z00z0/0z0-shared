using System.ComponentModel;
using System.Reflection;
using System.Text;
using Xunit;
using ZeroZero.Primitives;

namespace ZeroZero.Update.Tests;

/// <summary>The service end to end against a loopback release server and files signed here: the
/// check, the download into a fresh directory, the verification, the launch through a recorder
/// that starts nothing, and the sweep. No installer runs and nothing leaves the machine.</summary>
public class UpdateServiceTests(SignedFileFactory files) : IClassFixture<SignedFileFactory>, IDisposable
{
    private const string LatestPath = "/repos/studio/product/releases/latest";
    private const string Installer = "Product-Setup-1.2.3.exe";
    private const string DownloadPath = "/download/" + Installer;

    private readonly string _prefix = "ZeroZero.Update.Tests-" + Guid.NewGuid().ToString("N")[..8];
    private readonly LocalReleaseServer _server = new();
    private readonly RecordingLogSink _log = new();
    private readonly RecordingLauncher _launcher = new();

    private UpdateService Service(Version? running = null, ExpectedSigner? signer = null) => new(new UpdateOptions
    {
        RepositoryOwner = "studio",
        RepositoryName = "product",
        ProductName = "Product Name",
        RunningVersion = running ?? new Version(1, 0, 0),
        ExpectedSigner = signer ?? files.Signer,
        DirectoryPrefix = _prefix,
        InstallerFileName = "Product-Setup-{version}.exe",
        InstallerArguments = "/quiet",
        ApiBaseUri = _server.BaseUri,
        RequestTimeout = TimeSpan.FromSeconds(10),
        DownloadTimeout = TimeSpan.FromSeconds(10),
        Log = _log,
    }, launcher: _launcher);

    private string Body(string hash) => $"## Product v1.2.3\n\n- a thing\n\nDownload `{Installer}` below.\n\n**SHA256 (installer):** `{hash}`\n";

    /// <summary>A release whose installer is the file at <paramref name="path"/>, published with
    /// <paramref name="hash"/> (the file's own hash when null) and served in full.</summary>
    private void PublishRelease(string path, string? hash = null, string? body = null, long? declaredSize = null, bool serveAsset = true)
    {
        byte[] bytes = files.Bytes(path);
        _server.MapJson(LatestPath, _server.ReleaseJson("v1.2.3", body ?? Body(hash ?? files.Sha256(path)), (Installer, declaredSize ?? bytes.Length)));
        if (serveAsset) _server.MapFile(DownloadPath, bytes);
    }

    private IEnumerable<string> DownloadDirectories() =>
        Directory.EnumerateDirectories(Path.GetTempPath(), _prefix + "-*");

    [Fact]
    public async Task Check_ReportsANewerRelease()
    {
        PublishRelease(files.SignedByExpectedPath);
        using UpdateService service = Service();

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal(new Version(1, 2, 3, 0), result.Release!.Version);
        Assert.Equal(new Version(1, 0, 0, 0), result.RunningVersion);
        LocalReleaseServer.Request request = Assert.Single(_server.Requests);
        Assert.Contains("Product-Name/1.0.0.0", request.Headers["User-Agent"]);
        Assert.Contains("application/vnd.github+json", request.Headers["Accept"]);
        Assert.Contains(_log.Infos, line => line.Contains("v1.2.3 is available"));
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.0")]
    [InlineData("1.2.4")]
    [InlineData("2.0")]
    public async Task Check_ReportsUpToDateForTheSameOrANewerRunningVersion(string running)
    {
        PublishRelease(files.SignedByExpectedPath);
        using UpdateService service = Service(Version.Parse(running));

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.UpToDate, result.Outcome);
        Assert.NotNull(result.Release);
    }

    [Fact]
    public async Task Check_ReportsNoReleasesWithoutAnError()
    {
        _server.Map(LatestPath, new LocalReleaseServer.Response(404, Encoding.UTF8.GetBytes("""{"message":"Not Found"}""")));
        using UpdateService service = Service();

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.NoReleases, result.Outcome);
        Assert.Null(result.Error);
        Assert.Empty(_log.Errors);
    }

    [Fact]
    public async Task Check_ReportsTheRateLimit()
    {
        _server.Map(LatestPath, new LocalReleaseServer.Response(403, Encoding.UTF8.GetBytes("{}"),
            new Dictionary<string, string> { ["X-RateLimit-Remaining"] = "0", ["X-RateLimit-Reset"] = "1788388349" }));
        using UpdateService service = Service();

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.RateLimited, result.Outcome);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788388349), result.RateLimitResetsAt);
    }

    [Fact]
    public async Task Check_ReportsAnUnreachableService()
    {
        using UpdateService service = new(new UpdateOptions
        {
            RepositoryOwner = "studio",
            RepositoryName = "product",
            ProductName = "Product",
            RunningVersion = new Version(1, 0, 0),
            ExpectedSigner = files.Signer,
            DirectoryPrefix = _prefix,
            InstallerFileName = Installer,
            ApiBaseUri = LocalReleaseServer.ClosedPort(),
            Log = _log,
        }, launcher: _launcher);

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.Unreachable, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Prepare_DownloadsIntoAFreshDirectoryVerifiesAndLaunches()
    {
        PublishRelease(files.SignedByExpectedPath);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();
        var progress = new List<DownloadProgress>();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!, new SynchronousProgress(progress.Add));

        Assert.Equal(PrepareOutcome.Ready, prepared.Outcome);
        Assert.True(prepared.IsReady);
        Assert.Equal(VerificationVerdict.Verified, prepared.Verification!.Verdict);
        Assert.Equal(files.Sha256(files.SignedByExpectedPath), prepared.ExpectedSha256);
        string path = Assert.IsType<string>(prepared.InstallerPath);
        Assert.True(File.Exists(path));
        Assert.Equal(Installer, Path.GetFileName(path));
        string directory = Path.GetDirectoryName(path)!;
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), _prefix + "-"), directory);
        Assert.Single(DownloadDirectories());
        Assert.Equal(files.Bytes(files.SignedByExpectedPath), await File.ReadAllBytesAsync(path));
        Assert.NotEmpty(progress);

        LaunchResult launch = service.Launch(prepared);

        Assert.True(launch.Started);
        (string started, string arguments) = Assert.Single(_launcher.Started);
        Assert.Equal(path, started);
        Assert.Equal("/quiet", arguments);
    }

    [Fact]
    public async Task Prepare_RefusesAWrongHashAndRemovesTheFile()
    {
        PublishRelease(files.SignedByExpectedPath, hash: files.Sha256(files.SignedByOtherPath));
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.Refused, prepared.Outcome);
        Assert.False(prepared.IsReady);
        Assert.Null(prepared.InstallerPath);
        Assert.Equal(VerificationVerdict.HashMismatch, prepared.Verification!.Verdict);
        Assert.Empty(DownloadDirectories());
        Assert.False(service.Launch(prepared).Started);
        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public async Task Prepare_DownloadsNothingWhenTheReleasePublishesNoHash()
    {
        PublishRelease(files.SignedByExpectedPath, body: "## v1.2.3\n\nNotes without a hash.");
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.HashNotPublished, prepared.Outcome);
        Assert.False(prepared.IsReady);
        Assert.Equal(0, _server.RequestsFor(DownloadPath));
        Assert.Empty(DownloadDirectories());
        Assert.Contains("nothing was downloaded", prepared.Detail);
    }

    [Fact]
    public async Task Prepare_DownloadsNothingWhenTheHashIsAmbiguous()
    {
        string body = Body(files.Sha256(files.SignedByExpectedPath)) + $"\nAlso: {files.Sha256(files.SignedByOtherPath)}";
        PublishRelease(files.SignedByExpectedPath, body: body);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.HashAmbiguous, prepared.Outcome);
        Assert.Equal(0, _server.RequestsFor(DownloadPath));
    }

    [Fact]
    public async Task Prepare_ReportsAReleaseWithoutTheInstallerAsset()
    {
        byte[] bytes = files.Bytes(files.SignedByExpectedPath);
        _server.MapJson(LatestPath, _server.ReleaseJson("v1.2.3", Body(files.Sha256(files.SignedByExpectedPath)), ("Product-Setup-1.2.3.msi", bytes.Length), ("Product-Setup-1.2.3.exe.sha256", 64)));
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.InstallerAssetMissing, prepared.Outcome);
        Assert.Equal(Installer, prepared.InstallerFileName);
        Assert.Contains(Installer, prepared.Detail);
        Assert.Empty(DownloadDirectories());
    }

    [Fact]
    public async Task Prepare_ReportsADownloadThatEndsEarly()
    {
        byte[] bytes = files.Bytes(files.SignedByExpectedPath);
        PublishRelease(files.SignedByExpectedPath, serveAsset: false);
        _server.MapFile(DownloadPath, bytes[..(bytes.Length / 2)], declaredLength: bytes.Length);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.DownloadFailed, prepared.Outcome);
        Assert.Null(prepared.Verification);
        Assert.Empty(DownloadDirectories());
        Assert.Single(_log.Errors);
    }

    [Fact]
    public async Task Prepare_ReportsADownloadShorterThanTheReleaseDeclares()
    {
        PublishRelease(files.SignedByExpectedPath, declaredSize: files.Bytes(files.SignedByExpectedPath).Length + 100);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.DownloadFailed, prepared.Outcome);
        Assert.Contains("declares", prepared.Detail);
        Assert.Empty(DownloadDirectories());
    }

    [Fact]
    public async Task Prepare_RefusesAFileSignedBySomeoneElse()
    {
        PublishRelease(files.SignedByOtherPath);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.Refused, prepared.Outcome);
        Assert.Equal(VerificationVerdict.SignerMismatch, prepared.Verification!.Verdict);
        Assert.Empty(DownloadDirectories());
    }

    [Fact]
    public async Task Prepare_RefusesAnUnsignedFile()
    {
        PublishRelease(files.UnsignedPath);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.Refused, prepared.Outcome);
        Assert.Equal(VerificationVerdict.NotSigned, prepared.Verification!.Verdict);
        Assert.Empty(DownloadDirectories());
    }

    [Fact]
    public async Task Prepare_RefusesATamperedFileWhosePublishedHashMatchesIt()
    {
        PublishRelease(files.TamperedPath);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.Refused, prepared.Outcome);
        Assert.Equal(VerificationVerdict.SignatureInvalid, prepared.Verification!.Verdict);
    }

    [Fact]
    public async Task Prepare_RefusesTheExpectedSignerWhenNoCertificateIsPinned()
    {
        PublishRelease(files.SignedByExpectedPath);
        using UpdateService service = Service(signer: files.SignerUnpinned);
        UpdateCheckResult check = await service.CheckAsync();

        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        Assert.Equal(PrepareOutcome.Refused, prepared.Outcome);
        Assert.Equal(VerificationVerdict.CertificateNotPinned, prepared.Verification!.Verdict);
    }

    [Fact]
    public async Task Launch_RefusesAFileThatChangedAfterItWasPrepared()
    {
        PublishRelease(files.SignedByExpectedPath);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();
        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);
        Assert.True(prepared.IsReady);
        await File.WriteAllBytesAsync(prepared.InstallerPath!, files.Bytes(files.SignedByOtherPath));

        LaunchResult launch = service.Launch(prepared);

        Assert.False(launch.Started);
        Assert.Contains("refused at launch", launch.Detail);
        Assert.Empty(_launcher.Started);
        Assert.False(File.Exists(prepared.InstallerPath));
    }

    [Fact]
    public async Task Launch_ReportsALauncherThatFails()
    {
        PublishRelease(files.SignedByExpectedPath);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();
        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);
        _launcher.Fail = true;

        LaunchResult launch = service.Launch(prepared);

        Assert.False(launch.Started);
        Assert.Contains("could not be started", launch.Detail);
        Assert.Single(_log.Errors);
    }

    [Fact]
    public async Task Sweep_RemovesEarlierDownloadsAndKeepsTheCurrentOne()
    {
        string earlier = DownloadDirectory.Create(_prefix);
        File.WriteAllText(Path.Combine(earlier, Installer), "old");
        Directory.SetLastWriteTimeUtc(earlier, DateTime.UtcNow.AddDays(-1));
        PublishRelease(files.SignedByExpectedPath);
        using UpdateService service = Service();
        UpdateCheckResult check = await service.CheckAsync();
        PreparedUpdate prepared = await service.PrepareAsync(check.Release!);

        int removed = service.SweepStaleDownloads(TimeSpan.Zero);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(earlier));
        Assert.True(File.Exists(prepared.InstallerPath));
    }

    [Fact]
    public void RunningVersion_DefaultsToTheEntryAssemblyAndNeverToTheLibrary()
    {
        using UpdateService service = new(new UpdateOptions
        {
            RepositoryOwner = "studio",
            RepositoryName = "product",
            ProductName = "Product",
            ExpectedSigner = files.Signer,
            DirectoryPrefix = _prefix,
            InstallerFileName = Installer,
        });

        Assembly entry = Assembly.GetEntryAssembly()!;
        string text = AssemblyVersionText.Read(entry);
        int cut = text.IndexOfAny(['+', '-']);
        Version expected = VersionTag.Normalise(Version.Parse(cut >= 0 ? text[..cut] : text));

        Assert.Equal(expected, service.RunningVersion);
        Assert.NotEqual(VersionTag.Normalise(typeof(UpdateService).Assembly.GetName().Version!), service.RunningVersion);
    }

    [Fact]
    public void Options_AreValidatedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new UpdateService(new UpdateOptions
        {
            RepositoryOwner = "studio",
            RepositoryName = "product",
            ProductName = "Product",
            ExpectedSigner = files.Signer,
            DirectoryPrefix = "has space",
            InstallerFileName = Installer,
        }));
        Assert.Throws<ArgumentException>(() => new UpdateService(new UpdateOptions
        {
            RepositoryOwner = "studio",
            RepositoryName = "product",
            ProductName = "Product",
            ExpectedSigner = files.Signer,
            DirectoryPrefix = "Product",
            InstallerFileName = @"..\evil.exe",
        }));
    }

    [Theory]
    [InlineData("Hyper-V Manager Tray", "Hyper-V-Manager-Tray")]
    [InlineData("ChargeKeeper", "ChargeKeeper")]
    [InlineData("  ", "app")]
    public void Token_MakesAProductNameAUserAgentToken(string name, string token)
    {
        Assert.Equal(token, UpdateService.Token(name));
    }

    public void Dispose()
    {
        _server.Dispose();
        foreach (string directory in DownloadDirectories())
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Left for the temporary folder's own housekeeping.
            }
        }
    }

    private sealed class RecordingLauncher : IInstallerLauncher
    {
        public List<(string Path, string Arguments)> Started { get; } = [];
        public bool Fail { get; set; }

        public void Start(string path, string arguments)
        {
            if (Fail) throw new Win32Exception(2, "The system cannot find the file specified.");
            Started.Add((path, arguments));
        }
    }

    private sealed class SynchronousProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
