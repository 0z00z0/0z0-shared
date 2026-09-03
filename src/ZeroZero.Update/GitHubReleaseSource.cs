using System.Globalization;
using System.Net;
using System.Text.Json;
using ZeroZero.Primitives;

namespace ZeroZero.Update;

/// <summary>The latest release of one repository, from GitHub's releases API. Drafts and
/// pre-releases never appear: <c>releases/latest</c> excludes them.</summary>
public sealed class GitHubReleaseSource : IReleaseSource
{
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly ILogSink _log;

    /// <param name="http">A client the host owns, already carrying the user agent. Its own timeout
    /// is not used; the request runs under <see cref="UpdateOptions.RequestTimeout"/> through a
    /// cancellation token, so a hang and a caller's cancellation are told apart.</param>
    public GitHubReleaseSource(HttpClient http, UpdateOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        Uri = new Uri(options.ApiBaseUri, $"repos/{System.Uri.EscapeDataString(options.RepositoryOwner)}/{System.Uri.EscapeDataString(options.RepositoryName)}/releases/latest");
        _timeout = options.RequestTimeout;
        _log = options.Log;
    }

    public Uri Uri { get; }

    public async Task<ReleaseLookup> LookupLatestAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(Uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return Unreachable($"no answer from {Uri.Host} within {_timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s", ex);
        }
        catch (HttpRequestException ex)
        {
            return Unreachable(ex.Message, ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new ReleaseLookup(ReleaseLookupOutcome.NoReleases, Detail: "the repository has published no release");

            if (IsRateLimited(response, out DateTimeOffset? resetsAt))
            {
                _log.Info($"Update check refused by the rate limit; it lifts at {resetsAt?.ToString("u", CultureInfo.InvariantCulture) ?? "an unknown time"}.");
                return new ReleaseLookup(ReleaseLookupOutcome.RateLimited, RateLimitResetsAt: resetsAt, Detail: "GitHub's rate limit refused the request");
            }

            if (!response.IsSuccessStatusCode)
                return new ReleaseLookup(ReleaseLookupOutcome.InvalidResponse, Detail: $"HTTP {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)} from {Uri.Host}");

            string json;
            try
            {
                json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException)
            {
                return Unreachable($"the answer from {Uri.Host} ended early: {ex.Message}", ex);
            }

            return Parse(json);
        }
    }

    private static ReleaseLookup Unreachable(string detail, Exception error) =>
        new(ReleaseLookupOutcome.Unreachable, Detail: detail, Error: error);

    /// <summary>GitHub answers 403 or 429 with <c>X-RateLimit-Remaining: 0</c> and the reset as
    /// Unix seconds, or with <c>Retry-After</c>. A 403 carrying neither is not the limit.</summary>
    internal static bool IsRateLimited(HttpResponseMessage response, out DateTimeOffset? resetsAt)
    {
        resetsAt = null;
        int status = (int)response.StatusCode;
        if (status != 403 && status != 429) return false;

        if (TryHeader(response, "X-RateLimit-Remaining", out string remaining) && remaining.Trim() == "0")
        {
            if (TryHeader(response, "X-RateLimit-Reset", out string reset)
                && long.TryParse(reset.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSeconds))
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return true;
        }

        if (response.Headers.RetryAfter is { } retry)
        {
            resetsAt = retry.Date ?? (retry.Delta is { } delta ? DateTimeOffset.UtcNow + delta : null);
            return true;
        }

        return status == 429;
    }

    private static bool TryHeader(HttpResponseMessage response, string name, out string value)
    {
        if (response.Headers.TryGetValues(name, out IEnumerable<string>? values) && values.FirstOrDefault() is { } first)
        {
            value = first;
            return true;
        }
        value = "";
        return false;
    }

    /// <summary>The release JSON to a <see cref="ReleaseInfo"/>. A body that is not JSON, has no
    /// tag, or has a tag that is not a version is an invalid response, never a release.</summary>
    internal static ReleaseLookup Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new ReleaseLookup(ReleaseLookupOutcome.InvalidResponse, Detail: "the answer is not a release object");

            string? tag = StringOf(root, "tag_name");
            if (string.IsNullOrWhiteSpace(tag))
                return new ReleaseLookup(ReleaseLookupOutcome.InvalidResponse, Detail: "the release has no tag");
            if (!VersionTag.TryParse(tag, out Version version))
                return new ReleaseLookup(ReleaseLookupOutcome.InvalidResponse, Detail: $"the release tag '{tag}' is not a version");

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out JsonElement assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assetsElement.EnumerateArray())
                {
                    string? name = StringOf(asset, "name");
                    string? url = StringOf(asset, "browser_download_url");
                    if (name is null || url is null || !System.Uri.TryCreate(url, UriKind.Absolute, out Uri? downloadUri)) continue;

                    long size = asset.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.ValueKind == JsonValueKind.Number && sizeElement.TryGetInt64(out long parsed)
                        ? parsed
                        : 0;
                    assets.Add(new ReleaseAsset(name, size, downloadUri));
                }
            }

            Uri? htmlUri = System.Uri.TryCreate(StringOf(root, "html_url"), UriKind.Absolute, out Uri? html) ? html : null;
            DateTimeOffset? publishedAt = DateTimeOffset.TryParse(StringOf(root, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset published)
                ? published
                : null;

            var release = new ReleaseInfo(
                tag, version, VersionTag.NumberOf(tag),
                StringOf(root, "name"),
                StringOf(root, "body") ?? "",
                htmlUri, publishedAt, assets);
            return new ReleaseLookup(ReleaseLookupOutcome.Found, release);
        }
        catch (JsonException ex)
        {
            return new ReleaseLookup(ReleaseLookupOutcome.InvalidResponse, Detail: "the answer is not JSON", Error: ex);
        }
    }

    private static string? StringOf(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
