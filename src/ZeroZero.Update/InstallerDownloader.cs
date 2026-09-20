using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using ZeroZero.Primitives;

namespace ZeroZero.Update;

/// <summary>How far a download has come. <see cref="TotalBytes"/> is null when neither the
/// response nor the release says, and <see cref="Fraction"/> is null with it: an unknown total is
/// reported as unknown rather than as a number that would be a guess.</summary>
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
    /// <summary>How long a reporter waits between reports while bytes are arriving, measured from
    /// the last report sent. It bounds what a surface is asked to redraw — four times a second at
    /// most — whatever the file's size or the line's speed, which a step counted in bytes cannot:
    /// the same step is silent for a minute on a slow line and a flood on a fast one. The report
    /// carrying the final count is sent regardless of it.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

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

    /// <param name="progress">Where progress goes, or null for none. A download with none reads no
    /// clock and allocates nothing for it. Reports arrive no more often than
    /// <see cref="ProgressInterval"/>, and a download that completes ends with one carrying its
    /// true byte count; one that fails or is cancelled sends nothing after its last report.</param>
    /// <returns>The path of the complete file.</returns>
    /// <exception cref="DownloadException">Anything short of a complete file.</exception>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
    public async Task<string> DownloadAsync(ReleaseAsset asset, string directory, string fileName, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
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
            // Read only where a reporter is attached, so a download with none is the download it
            // was before the interval existed.
            long reportedAt = progress is null ? 0 : Stopwatch.GetTimestamp();

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
                        if (progress is not null && Stopwatch.GetElapsedTime(reportedAt) >= ProgressInterval)
                        {
                            reportedAt = Stopwatch.GetTimestamp();
                            progress.Report(new DownloadProgress(received, total));
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            if (asset.Size > 0 && received != asset.Size)
                throw new DownloadException($"{asset.Name} arrived as {received.ToString(CultureInfo.InvariantCulture)} bytes; the release declares {asset.Size.ToString(CultureInfo.InvariantCulture)}.");

            // Last, and only for a download that is whole: the interval can leave the final bytes
            // unreported, and a bar that stops short of its end is read as a download that stalled.
            // A failure or a cancellation leaves the method before reaching this line, so no report
            // follows one and none can claim more than arrived.
            progress?.Report(new DownloadProgress(received, total));

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
