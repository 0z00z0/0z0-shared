using ZeroZero.Brand.Core;
using CoreBrand = ZeroZero.Brand.Core.Brand;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>The fabricated application the rig's About content, update windows and settings shell
/// all present: one name, one version, one repository. Only the description differs per scenario.</summary>
internal static class SampleInfo
{
    public const string AppName = "Brand Test Harness";

    private const string Version = "0.0.0-dev";

    /// <summary>Where the release notes are fetched from when a run names no other address.</summary>
    public static string DefaultNotesUrl => $"{CoreBrand.OrgUrl}/0z0-shared/releases/latest/download/whats-new.txt";

    /// <summary>The credits both About surfaces list.</summary>
    public static readonly ExternalLibrary[] Libraries =
    [
        new("Microsoft.WindowsAppSDK", "Microsoft", "WinUI 3 / Windows App SDK runtime", "MIT", "https://github.com/microsoft/WindowsAppSDK"),
        new("H.NotifyIcon.WinUI", "HavenDV", "The notify-icon library behind the tray host, a dependency of this harness through ZeroZero.Tray.WinUI", "MIT", "https://github.com/HavenDV/H.NotifyIcon"),
    ];

    /// <summary>The sample application's About data. Notes address and credits are left out unless a
    /// scenario shows them.</summary>
    public static AboutInfo Create(string description, string appName = AppName,
                                   string? releaseNotesUrl = null, bool withLibraries = false) => new()
    {
        AppName = appName,
        Version = Version,
        Description = description,
        RepoUrl = $"{CoreBrand.OrgUrl}/0z0-shared",
        ReleaseNotesUrl = releaseNotesUrl,
        ExternalLibraries = withLibraries ? Libraries : [],
    };
}
