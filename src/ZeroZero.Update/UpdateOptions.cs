using ZeroZero.Primitives;

namespace ZeroZero.Update;

/// <summary>What the application supplies to the update flow. Every product string lives here;
/// the component carries none of its own.</summary>
public sealed class UpdateOptions
{
    /// <summary>The GitHub account or organisation the releases are published under.</summary>
    public required string RepositoryOwner { get; init; }

    /// <summary>The repository whose releases are checked.</summary>
    public required string RepositoryName { get; init; }

    /// <summary>The product name the user agent carries. GitHub refuses a request without one.</summary>
    public required string ProductName { get; init; }

    /// <summary>The version the application is running. Null reads the entry assembly — the
    /// application, never this library — through <see cref="AssemblyVersionText"/>.</summary>
    public Version? RunningVersion { get; init; }

    /// <summary>Who must have signed the installer. The only input to the signature check; neither
    /// check can be turned off.</summary>
    public required ExpectedSigner ExpectedSigner { get; init; }

    /// <summary>The first part of the download directory's name under the temporary folder; the
    /// rest is a fresh identifier per download. Letters, digits, dots and hyphens.</summary>
    public required string DirectoryPrefix { get; init; }

    /// <summary>The installer asset's file name, with <c>{version}</c> where the release's version
    /// goes — <c>Product-Setup-{version}.exe</c>. The release must carry an asset of exactly that
    /// name; the first executable found is never taken.</summary>
    public required string InstallerFileName { get; init; }

    /// <summary>Arguments the installer is started with. None by default.</summary>
    public string InstallerArguments { get; init; } = "";

    /// <summary>How long after <see cref="UpdateScheduler.Start"/> the first check runs.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The time between one check finishing and the next starting. Counted from process
    /// start, never persisted: the component stores nothing.</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>The whole of one API request, headers to body.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The whole of one installer download.</summary>
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Where the releases API is. GitHub's, unless a test points it elsewhere.</summary>
    public Uri ApiBaseUri { get; init; } = new("https://api.github.com/");

    public ILogSink Log { get; init; } = NullLogSink.Instance;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryOwner, nameof(RepositoryOwner));
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryName, nameof(RepositoryName));
        ArgumentException.ThrowIfNullOrWhiteSpace(ProductName, nameof(ProductName));
        ArgumentNullException.ThrowIfNull(ExpectedSigner, nameof(ExpectedSigner));
        ArgumentException.ThrowIfNullOrWhiteSpace(InstallerFileName, nameof(InstallerFileName));
        DownloadDirectory.ValidatePrefix(DirectoryPrefix);
        if (InstallerFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || InstallerFileName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"'{InstallerFileName}' is not a file name.", nameof(InstallerFileName));
        if (!ApiBaseUri.IsAbsoluteUri)
            throw new ArgumentException("The API base must be an absolute URI.", nameof(ApiBaseUri));
    }
}
