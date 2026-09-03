using System.Net;
using System.Text;
using Xunit;

namespace ZeroZero.Update.Tests;

public class GitHubReleaseSourceTests
{
    private const string Hash = "AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084";

    private static UpdateOptions Options(Uri api, TimeSpan? timeout = null, RecordingLogSink? log = null) => new()
    {
        RepositoryOwner = "studio",
        RepositoryName = "product",
        ProductName = "Product",
        ExpectedSigner = new ExpectedSigner("CN=Test"),
        DirectoryPrefix = "Product-update",
        InstallerFileName = "Product-Setup-{version}.exe",
        ApiBaseUri = api,
        RequestTimeout = timeout ?? TimeSpan.FromSeconds(10),
        Log = log ?? new RecordingLogSink(),
    };

    private static HttpClient Client() => new() { Timeout = Timeout.InfiniteTimeSpan };

    [Fact]
    public void Uri_IsTheRepositorysLatestRelease()
    {
        var source = new GitHubReleaseSource(Client(), Options(new Uri("https://api.github.com/")));

        Assert.Equal("https://api.github.com/repos/studio/product/releases/latest", source.Uri.AbsoluteUri);
    }

    [Fact]
    public void Parse_ReadsTheReleaseAndItsAssets()
    {
        string json = """
            {
              "tag_name": "v1.35.0",
              "name": "ChargeKeeper v1.35.0",
              "body": "notes",
              "html_url": "https://example.invalid/releases/tag/v1.35.0",
              "published_at": "2026-09-02T14:50:33Z",
              "assets": [
                { "name": "0z00z0.Product.installer.yaml", "size": 1086, "browser_download_url": "https://example.invalid/download/v1.35.0/0z00z0.Product.installer.yaml" },
                { "name": "Product-Setup-1.35.0.exe", "size": 61944760, "browser_download_url": "https://example.invalid/download/v1.35.0/Product-Setup-1.35.0.exe" }
              ]
            }
            """;

        ReleaseLookup lookup = GitHubReleaseSource.Parse(json);

        Assert.Equal(ReleaseLookupOutcome.Found, lookup.Outcome);
        ReleaseInfo release = Assert.IsType<ReleaseInfo>(lookup.Release);
        Assert.Equal("v1.35.0", release.TagName);
        Assert.Equal(new Version(1, 35, 0, 0), release.Version);
        Assert.Equal("1.35.0", release.VersionText);
        Assert.Equal("ChargeKeeper v1.35.0", release.Name);
        Assert.Equal("notes", release.Body);
        Assert.Equal("https://example.invalid/releases/tag/v1.35.0", release.HtmlUri?.AbsoluteUri);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 14, 50, 33, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal(2, release.Assets.Count);
        ReleaseAsset installer = Assert.IsType<ReleaseAsset>(release.FindAsset("Product-Setup-1.35.0.exe"));
        Assert.Equal(61944760, installer.Size);
        Assert.Equal("https://example.invalid/download/v1.35.0/Product-Setup-1.35.0.exe", installer.DownloadUri.AbsoluteUri);
        Assert.Null(release.FindAsset("Product-Setup-1.35.0.EXE"));
    }

    [Fact]
    public void Parse_ToleratesAnAbsentBodyAndNoAssets()
    {
        ReleaseLookup lookup = GitHubReleaseSource.Parse("""{ "tag_name": "v2.0.0", "body": null }""");

        Assert.Equal(ReleaseLookupOutcome.Found, lookup.Outcome);
        Assert.Equal("", lookup.Release!.Body);
        Assert.Empty(lookup.Release.Assets);
        Assert.Null(lookup.Release.HtmlUri);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "tag_name": "" }""")]
    [InlineData("""{ "tag_name": "nightly" }""")]
    [InlineData("""{ "tag_name": "v1.2.3-beta.1" }""")]
    public void Parse_RefusesWhatIsNotARelease(string json)
    {
        ReleaseLookup lookup = GitHubReleaseSource.Parse(json);

        Assert.Equal(ReleaseLookupOutcome.InvalidResponse, lookup.Outcome);
        Assert.Null(lookup.Release);
        Assert.NotEqual("", lookup.Detail);
    }

    [Fact]
    public void IsRateLimited_ReadsGitHubsHeadersInTheInvariantCulture()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("X-RateLimit-Remaining", "0");
        response.Headers.Add("X-RateLimit-Reset", "1788388349");

        Assert.True(GitHubReleaseSource.IsRateLimited(response, out DateTimeOffset? resetsAt));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788388349), resetsAt);
    }

    [Fact]
    public void IsRateLimited_TakesRetryAfterOn429()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("Retry-After", "120");

        Assert.True(GitHubReleaseSource.IsRateLimited(response, out DateTimeOffset? resetsAt));
        Assert.NotNull(resetsAt);
        Assert.InRange(resetsAt.Value - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void IsRateLimited_IsNotAPlainForbidden()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("X-RateLimit-Remaining", "57");

        Assert.False(GitHubReleaseSource.IsRateLimited(response, out _));
    }

    [Fact]
    public async Task LookupLatest_FindsTheReleaseAndSendsTheHeadersGitHubWants()
    {
        using var server = new LocalReleaseServer();
        server.MapJson("/repos/studio/product/releases/latest", server.ReleaseJson("v1.2.3", $"SHA256 `{Hash}`", ("Product-Setup-1.2.3.exe", 10)));
        using HttpClient client = Client();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Product/1.0.0");
        var source = new GitHubReleaseSource(client, Options(server.BaseUri));

        ReleaseLookup lookup = await source.LookupLatestAsync();

        Assert.Equal(ReleaseLookupOutcome.Found, lookup.Outcome);
        Assert.Equal("v1.2.3", lookup.Release!.TagName);
        LocalReleaseServer.Request request = Assert.Single(server.Requests);
        Assert.Contains("Product/1.0.0", request.Headers["User-Agent"]);
    }

    [Fact]
    public async Task LookupLatest_ReportsNoReleasesOnNotFound()
    {
        using var server = new LocalReleaseServer();
        server.Map("/repos/studio/product/releases/latest", new LocalReleaseServer.Response(404, Encoding.UTF8.GetBytes("""{"message":"Not Found"}""")));
        var source = new GitHubReleaseSource(Client(), Options(server.BaseUri));

        ReleaseLookup lookup = await source.LookupLatestAsync();

        Assert.Equal(ReleaseLookupOutcome.NoReleases, lookup.Outcome);
    }

    [Fact]
    public async Task LookupLatest_ReportsTheRateLimitWithItsReset()
    {
        using var server = new LocalReleaseServer();
        server.Map("/repos/studio/product/releases/latest", new LocalReleaseServer.Response(403, Encoding.UTF8.GetBytes("""{"message":"API rate limit exceeded"}"""),
            new Dictionary<string, string> { ["X-RateLimit-Remaining"] = "0", ["X-RateLimit-Reset"] = "1788388349" }));
        var log = new RecordingLogSink();
        var source = new GitHubReleaseSource(Client(), Options(server.BaseUri, log: log));

        ReleaseLookup lookup = await source.LookupLatestAsync();

        Assert.Equal(ReleaseLookupOutcome.RateLimited, lookup.Outcome);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788388349), lookup.RateLimitResetsAt);
        Assert.Contains(log.Infos, line => line.Contains("rate limit"));
    }

    [Fact]
    public async Task LookupLatest_ReportsAServerErrorAsInvalid()
    {
        using var server = new LocalReleaseServer();
        server.Map("/repos/studio/product/releases/latest", new LocalReleaseServer.Response(500, Encoding.UTF8.GetBytes("boom")));
        var source = new GitHubReleaseSource(Client(), Options(server.BaseUri));

        ReleaseLookup lookup = await source.LookupLatestAsync();

        Assert.Equal(ReleaseLookupOutcome.InvalidResponse, lookup.Outcome);
        Assert.Contains("500", lookup.Detail);
    }

    [Fact]
    public async Task LookupLatest_ReportsAClosedPortAsUnreachable()
    {
        var source = new GitHubReleaseSource(Client(), Options(LocalReleaseServer.ClosedPort()));

        ReleaseLookup lookup = await source.LookupLatestAsync();

        Assert.Equal(ReleaseLookupOutcome.Unreachable, lookup.Outcome);
        Assert.IsType<HttpRequestException>(lookup.Error);
    }

    [Fact]
    public async Task LookupLatest_GivesUpOnAServerThatNeverAnswers()
    {
        using var server = new LocalReleaseServer();
        server.Map("/repos/studio/product/releases/latest", new LocalReleaseServer.Response(200, Encoding.UTF8.GetBytes("{}"), Delay: TimeSpan.FromSeconds(10)));
        var source = new GitHubReleaseSource(Client(), Options(server.BaseUri, TimeSpan.FromMilliseconds(300)));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        ReleaseLookup lookup = await source.LookupLatestAsync();

        Assert.Equal(ReleaseLookupOutcome.Unreachable, lookup.Outcome);
        Assert.Contains("within", lookup.Detail);
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LookupLatest_ThrowsWhenTheCallerCancels()
    {
        using var server = new LocalReleaseServer();
        server.Map("/repos/studio/product/releases/latest", new LocalReleaseServer.Response(200, Encoding.UTF8.GetBytes("{}"), Delay: TimeSpan.FromSeconds(10)));
        var source = new GitHubReleaseSource(Client(), Options(server.BaseUri));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.LookupLatestAsync(cancel.Token));
    }
}
