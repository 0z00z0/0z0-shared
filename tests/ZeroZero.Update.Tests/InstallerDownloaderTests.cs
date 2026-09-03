using Xunit;

namespace ZeroZero.Update.Tests;

public class InstallerDownloaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ZeroZero.Update.Tests-dl-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingLogSink _log = new();
    private readonly HttpClient _client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public InstallerDownloaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public async Task Download_WritesTheWholeFileAndReportsProgress()
    {
        byte[] payload = Payload(300_000);
        using var server = new LocalReleaseServer();
        server.MapFile("/download/Product-Setup-1.2.3.exe", payload);
        var asset = new ReleaseAsset("Product-Setup-1.2.3.exe", payload.Length, new Uri(server.BaseUri, "download/Product-Setup-1.2.3.exe"));
        var downloader = new InstallerDownloader(_client, TimeSpan.FromSeconds(10), _log);
        var seen = new List<DownloadProgress>();

        string path = await downloader.DownloadAsync(asset, _directory, "Product-Setup-1.2.3.exe", new Progress(seen.Add));

        Assert.Equal(Path.Combine(_directory, "Product-Setup-1.2.3.exe"), path);
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.NotEmpty(seen);
        Assert.Equal(payload.Length, seen[^1].BytesReceived);
        Assert.Equal(payload.Length, seen[^1].TotalBytes);
        Assert.Equal(1.0, seen[^1].Fraction);
        Assert.True(seen.Zip(seen.Skip(1)).All(pair => pair.Second.BytesReceived >= pair.First.BytesReceived));
        Assert.Contains(_log.Infos, line => line.Contains("Downloaded"));
    }

    [Fact]
    public async Task Download_RefusesAFileThatEndsEarlyAndLeavesNothingBehind()
    {
        byte[] payload = Payload(100_000);
        using var server = new LocalReleaseServer();
        // The server declares the full length and closes the socket after half of it.
        server.MapFile("/download/Product-Setup-1.2.3.exe", payload[..50_000], declaredLength: payload.Length);
        var asset = new ReleaseAsset("Product-Setup-1.2.3.exe", payload.Length, new Uri(server.BaseUri, "download/Product-Setup-1.2.3.exe"));
        var downloader = new InstallerDownloader(_client, TimeSpan.FromSeconds(10), _log);

        await Assert.ThrowsAsync<DownloadException>(() => downloader.DownloadAsync(asset, _directory, "Product-Setup-1.2.3.exe", null));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Download_RefusesAFileShorterThanTheReleaseDeclares()
    {
        byte[] payload = Payload(20_000);
        using var server = new LocalReleaseServer();
        server.MapFile("/download/Product-Setup-1.2.3.exe", payload);
        var asset = new ReleaseAsset("Product-Setup-1.2.3.exe", payload.Length + 1, new Uri(server.BaseUri, "download/Product-Setup-1.2.3.exe"));
        var downloader = new InstallerDownloader(_client, TimeSpan.FromSeconds(10), _log);

        var error = await Assert.ThrowsAsync<DownloadException>(() => downloader.DownloadAsync(asset, _directory, "Product-Setup-1.2.3.exe", null));

        Assert.Contains("declares", error.Message);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Download_RefusesAnAssetTheServerDoesNotHave()
    {
        using var server = new LocalReleaseServer();
        var asset = new ReleaseAsset("Product-Setup-1.2.3.exe", 0, new Uri(server.BaseUri, "download/absent.exe"));
        var downloader = new InstallerDownloader(_client, TimeSpan.FromSeconds(10), _log);

        var error = await Assert.ThrowsAsync<DownloadException>(() => downloader.DownloadAsync(asset, _directory, "Product-Setup-1.2.3.exe", null));

        Assert.Contains("404", error.Message);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Download_GivesUpAfterItsTimeout()
    {
        using var server = new LocalReleaseServer();
        server.Map("/download/Product-Setup-1.2.3.exe", new LocalReleaseServer.Response(200, Payload(10), Delay: TimeSpan.FromSeconds(10)));
        var asset = new ReleaseAsset("Product-Setup-1.2.3.exe", 10, new Uri(server.BaseUri, "download/Product-Setup-1.2.3.exe"));
        var downloader = new InstallerDownloader(_client, TimeSpan.FromMilliseconds(300), _log);

        var error = await Assert.ThrowsAsync<DownloadException>(() => downloader.DownloadAsync(asset, _directory, "Product-Setup-1.2.3.exe", null));

        Assert.Contains("did not finish", error.Message);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Download_ThrowsWhenTheCallerCancels()
    {
        using var server = new LocalReleaseServer();
        server.Map("/download/Product-Setup-1.2.3.exe", new LocalReleaseServer.Response(200, Payload(10), Delay: TimeSpan.FromSeconds(10)));
        var asset = new ReleaseAsset("Product-Setup-1.2.3.exe", 10, new Uri(server.BaseUri, "download/Product-Setup-1.2.3.exe"));
        var downloader = new InstallerDownloader(_client, TimeSpan.FromSeconds(10), _log);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(asset, _directory, "Product-Setup-1.2.3.exe", null, cancel.Token));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        _client.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temporary folder's own housekeeping.
        }
    }

    /// <summary>Synchronous, so the last report has landed when the download returns.</summary>
    private sealed class Progress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
