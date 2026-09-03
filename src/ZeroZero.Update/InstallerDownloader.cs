using System.Buffers;
using System.Globalization;
using ZeroZero.Primitives;

namespace ZeroZero.Update;

/// <summary>How far a download has come. <see cref="TotalBytes"/> is null when neither the
/// response nor the release says.</summary>
public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? Math.Min(1.0, (double)BytesReceived / TotalBytes.Value) : null;
}

/// <summary>A download that did not complete: refused by the server, ended early, timed out, or
/// shorter than the release declares. The partial file is gone by the time this is thrown.</summary>
public sealed class DownloadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Fetches one release asset into a file, streaming, reporting progress, under an explicit
/// timeout. The file's length is checked against the size the release declares; a hash check is
/// the verifier's, afterwards.</summary>
public sealed class InstallerDownloader
{
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly ILogSink _log;

    public InstallerDownloader(HttpClient http, TimeSpan timeout, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(log);
        _http = http;
        _timeout = timeout;
        _log = log;
    }

    /// <returns>The path of the complete file.</returns>
    /// <exception cref="DownloadException">Anything short of a complete file.</exception>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
    public async Task<string> DownloadAsync(ReleaseAsset asset, string directory, string fileName, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string path = Path.Combine(directory, fileName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(asset.DownloadUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new DownloadException($"{asset.DownloadUri.Host} answered HTTP {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)} for {asset.Name}.");

            long? total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : null);
            long received = 0;

            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            await using (Stream body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 16);
                try
                {
                    int read;
                    while ((read = await body.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                        received += read;
                        progress?.Report(new DownloadProgress(received, total));
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            if (asset.Size > 0 && received != asset.Size)
                throw new DownloadException($"{asset.Name} arrived as {received.ToString(CultureInfo.InvariantCulture)} bytes; the release declares {asset.Size.ToString(CultureInfo.InvariantCulture)}.");

            _log.Info($"Downloaded {asset.Name} ({received.ToString(CultureInfo.InvariantCulture)} bytes).");
            return path;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Discard(path);
            throw;
        }
        catch (OperationCanceledException ex)
        {
            Discard(path);
            throw new DownloadException($"{asset.Name} did not finish within {_timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s.", ex);
        }
        catch (DownloadException)
        {
            Discard(path);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Discard(path);
            throw new DownloadException($"{asset.Name} could not be downloaded: {ex.Message}", ex);
        }
    }

    private static void Discard(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The sweep takes it later; the failure being reported is the download's.
        }
    }
}
