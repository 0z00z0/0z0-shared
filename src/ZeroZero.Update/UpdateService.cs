using System.ComponentModel;
using System.Net.Http.Headers;
using System.Reflection;
using ZeroZero.Primitives;

namespace ZeroZero.Update;

/// <summary>How far preparing an update got. Only <see cref="Ready"/> can be launched.</summary>
public enum PrepareOutcome
{
    /// <summary>Downloaded and verified; the file is in a fresh directory of its own.</summary>
    Ready,

    /// <summary>The release carries no asset of the installer's name. Nothing was downloaded.</summary>
    InstallerAssetMissing,

    /// <summary>The release publishes no SHA-256. Nothing was downloaded: a file that cannot be
    /// verified is not fetched.</summary>
    HashNotPublished,

    /// <summary>The release publishes more than one SHA-256. Nothing was downloaded.</summary>
    HashAmbiguous,

    /// <summary>The download did not complete; the partial file is gone.</summary>
    DownloadFailed,

    /// <summary>Downloaded, and verification refused it; the file is gone. <see cref="PreparedUpdate.Verification"/> says why.</summary>
    Refused,
}

/// <param name="InstallerFileName">The asset name the release was asked for, version substituted.</param>
/// <param name="InstallerPath">Where the verified file is, when <see cref="IsReady"/>; otherwise null.</param>
/// <param name="ExpectedSha256">The hash the release publishes, when it publishes one.</param>
public sealed record PreparedUpdate(
    PrepareOutcome Outcome,
    ReleaseInfo Release,
    string InstallerFileName,
    string? InstallerPath,
    string? ExpectedSha256,
    VerificationResult? Verification,
    string Detail)
{
    public bool IsReady => Outcome == PrepareOutcome.Ready && InstallerPath is not null && Verification is { IsVerified: true };
}

public sealed record LaunchResult(bool Started, string Detail);

/// <summary>The flow without its dialogs: check, prepare, launch, sweep. The orchestration above
/// it decides what to ask and when to exit.</summary>
public interface IUpdateService
{
    Version RunningVersion { get; }

    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads the release's installer into a fresh directory and verifies it. Never
    /// runs it.</summary>
    Task<PreparedUpdate> PrepareAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Verifies the prepared file again and starts it. Refuses anything that is not
    /// ready, and anything that changed since it was prepared.</summary>
    LaunchResult Launch(PreparedUpdate update);

    /// <summary>Removes download directories older builds or earlier runs left behind.</summary>
    int SweepStaleDownloads(TimeSpan olderThan);
}

/// <summary>The update flow over GitHub releases. One instance per application, owning its two
/// HTTP clients — one for the API, one for the download — for the life of the process.</summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
    private readonly UpdateOptions _options;
    private readonly ILogSink _log;
    private readonly HttpClient _api;
    private readonly HttpClient _download;
    private readonly IReleaseSource _source;
    private readonly InstallerDownloader _downloader;
    private readonly IInstallerLauncher _launcher;
    private string? _currentDirectory;

    /// <param name="source">Where releases come from; GitHub's API when null.</param>
    /// <param name="launcher">What starts the installer; the shell when null.</param>
    public UpdateService(UpdateOptions options, IReleaseSource? source = null, IInstallerLauncher? launcher = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _log = options.Log;
        RunningVersion = options.RunningVersion is { } given ? VersionTag.Normalise(given) : EntryAssemblyVersion();

        _api = NewClient(options, RunningVersion, "application/vnd.github+json");
        _api.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _download = NewClient(options, RunningVersion, "application/octet-stream");

        _source = source ?? new GitHubReleaseSource(_api, options);
        _downloader = new InstallerDownloader(_download, options.DownloadTimeout, _log);
        _launcher = launcher ?? new ShellInstallerLauncher();
    }

    public Version RunningVersion { get; }

    public UpdateOptions Options => _options;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        ReleaseLookup lookup = await _source.LookupLatestAsync(cancellationToken).ConfigureAwait(false);
        switch (lookup.Outcome)
        {
            case ReleaseLookupOutcome.Found:
            {
                ReleaseInfo release = lookup.Release!;
                bool newer = release.Version > RunningVersion;
                _log.Info(newer
                    ? $"Update check: {release.TagName} is available; {RunningVersion} is running."
                    : $"Update check: {RunningVersion} is running and {release.TagName} is the latest release.");
                return new UpdateCheckResult(newer ? UpdateCheckOutcome.UpdateAvailable : UpdateCheckOutcome.UpToDate, RunningVersion, release);
            }
            case ReleaseLookupOutcome.NoReleases:
                _log.Info("Update check: no release has been published.");
                return new UpdateCheckResult(UpdateCheckOutcome.NoReleases, RunningVersion, Detail: lookup.Detail);
            case ReleaseLookupOutcome.RateLimited:
                return new UpdateCheckResult(UpdateCheckOutcome.RateLimited, RunningVersion, RateLimitResetsAt: lookup.RateLimitResetsAt, Detail: lookup.Detail);
            case ReleaseLookupOutcome.Unreachable:
                _log.Info($"Update check did not complete: {lookup.Detail}.");
                return new UpdateCheckResult(UpdateCheckOutcome.Unreachable, RunningVersion, Detail: lookup.Detail, Error: lookup.Error);
            default:
                _log.Info($"Update check answered with something unexpected: {lookup.Detail}.");
                return new UpdateCheckResult(UpdateCheckOutcome.InvalidResponse, RunningVersion, Detail: lookup.Detail, Error: lookup.Error);
        }
    }

    public async Task<PreparedUpdate> PrepareAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        string fileName = _options.InstallerFileName.Replace("{version}", release.VersionText, StringComparison.Ordinal);

        // The hash is read before anything is fetched: a file that cannot be verified is not
        // downloaded, and a release that publishes none is refused where the user can see it.
        PublishedHash hash = PublishedHash.FromBody(release.Body);
        if (hash.Outcome == PublishedHashOutcome.NotPublished)
            return Refuse(PrepareOutcome.HashNotPublished, release, fileName, null,
                "the release publishes no SHA-256 for its installer, so a download could not be verified; nothing was downloaded");
        if (hash.Outcome == PublishedHashOutcome.Ambiguous)
            return Refuse(PrepareOutcome.HashAmbiguous, release, fileName, null,
                "the release publishes more than one SHA-256, so which is the installer's is a guess; nothing was downloaded");

        ReleaseAsset? asset = release.FindAsset(fileName);
        if (asset is null)
            return Refuse(PrepareOutcome.InstallerAssetMissing, release, fileName, hash.Sha256Hex,
                $"the release carries no file named {fileName}");

        string directory = DownloadDirectory.Create(_options.DirectoryPrefix);
        _currentDirectory = directory;

        string path;
        try
        {
            path = await _downloader.DownloadAsync(asset, directory, fileName, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (DownloadException ex)
        {
            Discard(directory);
            _log.Error(nameof(UpdateService), ex);
            return Refuse(PrepareOutcome.DownloadFailed, release, fileName, hash.Sha256Hex, ex.Message);
        }
        catch (OperationCanceledException)
        {
            Discard(directory);
            throw;
        }

        VerificationResult verification = InstallerVerifier.Verify(path, hash.Sha256Hex!, _options.ExpectedSigner);
        if (!verification.IsVerified)
        {
            Discard(directory);
            _log.Info($"Refused {fileName} ({verification.Verdict}): {verification.Detail}.");
            return new PreparedUpdate(PrepareOutcome.Refused, release, fileName, null, hash.Sha256Hex, verification, verification.Detail);
        }

        _log.Info($"Verified {fileName}: {verification.Detail}.");
        return new PreparedUpdate(PrepareOutcome.Ready, release, fileName, path, hash.Sha256Hex, verification, verification.Detail);
    }

    public LaunchResult Launch(PreparedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!update.IsReady || update.InstallerPath is null || update.ExpectedSha256 is null)
            return new LaunchResult(false, "the update was not prepared, so there is nothing verified to run");

        // Verified again at the moment of launch, so the bytes that were verified and the bytes
        // that run are the same bytes, or nothing runs.
        VerificationResult again = InstallerVerifier.Verify(update.InstallerPath, update.ExpectedSha256, _options.ExpectedSigner);
        if (!again.IsVerified)
        {
            Discard(Path.GetDirectoryName(update.InstallerPath));
            _log.Info($"Refused {update.InstallerFileName} at launch ({again.Verdict}): {again.Detail}.");
            return new LaunchResult(false, $"refused at launch: {again.Detail}");
        }

        try
        {
            _launcher.Start(update.InstallerPath, _options.InstallerArguments);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            _log.Error(nameof(UpdateService), ex);
            return new LaunchResult(false, $"the installer could not be started: {ex.Message}");
        }

        _log.Info($"Started {update.InstallerFileName} from {Path.GetDirectoryName(update.InstallerPath)}.");
        return new LaunchResult(true, "the installer is running");
    }

    public int SweepStaleDownloads(TimeSpan olderThan) =>
        DownloadDirectory.Sweep(_options.DirectoryPrefix, olderThan, _log, except: _currentDirectory);

    public void Dispose()
    {
        _api.Dispose();
        _download.Dispose();
    }

    private PreparedUpdate Refuse(PrepareOutcome outcome, ReleaseInfo release, string fileName, string? hash, string detail)
    {
        _log.Info($"Not installing {release.TagName} ({outcome}): {detail}.");
        return new PreparedUpdate(outcome, release, fileName, null, hash, null, detail);
    }

    private void Discard(string? directory)
    {
        try
        {
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(nameof(UpdateService), ex);
        }
    }

    /// <summary>The entry assembly's version, never this library's: the executing assembly is the
    /// library the moment this code is shared, and its version would silently stand in.</summary>
    private static Version EntryAssemblyVersion()
    {
        Assembly entry = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("The running version is unknown: there is no entry assembly to read it from. Set UpdateOptions.RunningVersion.");
        string text = AssemblyVersionText.Read(entry);
        int cut = text.IndexOfAny(['+', '-']);
        string number = cut >= 0 ? text[..cut] : text;
        if (!Version.TryParse(number, out Version? version))
            throw new InvalidOperationException($"The entry assembly reports '{text}', which is not a version. Set UpdateOptions.RunningVersion.");
        return VersionTag.Normalise(version);
    }

    private static HttpClient NewClient(UpdateOptions options, Version running, string accept)
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Token(options.ProductName), running.ToString()));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"(+https://github.com/{options.RepositoryOwner}/{options.RepositoryName})"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return client;
    }

    /// <summary>A product name as an HTTP token: letters, digits, dot, hyphen and underscore, so a
    /// name with spaces still makes a valid user agent.</summary>
    internal static string Token(string productName)
    {
        string token = string.Concat(productName.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-')).Trim('-');
        return token.Length > 0 ? token : "app";
    }
}
