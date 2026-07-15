using Microsoft.UI.Xaml;
using ZeroZero.Brand.Core;
// This project's own namespace nests inside ZeroZero.Brand (same collision documented in
// BrandAboutWindow.xaml.cs), so an unqualified "Brand" resolves to the namespace segment
// instead of ZeroZero.Brand.Core.Brand — alias it to sidestep that.
using CoreBrand = ZeroZero.Brand.Core.Brand;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Interactive manual test rig exercising both ways of hosting the shared About content, so each
/// can be eyeballed without building or running ChargeKeeper, HyperVManagerTray, or M365Migrator:
/// <list type="bullet">
/// <item><see cref="BrandAboutWindow"/> — the tray-app popup (Mica backdrop, centering, credits
/// expander, "Check for Updates").</item>
/// <item><see cref="HostedControlWindow"/> — <see cref="BrandAboutControl"/> embedded directly in
/// a plain window with ordinary chrome and no update button, simulating a full windowed app's
/// in-navigation About page.</item>
/// </list>
/// Both windows open at launch; the app exits once the last of the two is closed (default
/// <see cref="Application.DispatcherShutdownMode"/> is OnLastWindowClose).
/// </summary>
public partial class App : Application
{
    private Window? _aboutWindow;
    private Window? _hostedControlWindow;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var libraries = new ExternalLibrary[]
        {
            new("Microsoft.WindowsAppSDK", "Microsoft", "WinUI 3 / Windows App SDK runtime", "MIT", "https://github.com/microsoft/WindowsAppSDK"),
            new("H.NotifyIcon.WinUI", "HavenDV", "Example third-party credit (not an actual dependency of this harness)", "MIT", "https://github.com/HavenDV/H.NotifyIcon"),
        };

        var options = new BrandAboutOptions
        {
            Info = new AboutInfo
            {
                AppName     = "Brand Test Harness",
                Version     = "0.0.0-dev",
                Description = "Interactive launch-test rig for the shared BrandAboutWindow component — " +
                              "verifies the About box renders correctly on its own, before ChargeKeeper " +
                              "or HyperVManagerTray adopt it.",
                RepoUrl     = $"{CoreBrand.OrgUrl}/0z0-shared",
                ExternalLibraries = libraries,
            },
            // Present so the "Check for Updates" button is visible and clickable for the test —
            // omit this to verify the button hides itself instead (see BrandAboutWindow.xaml.cs).
            // Returns false (no update applied) so the window stays open for inspection rather than
            // driving the new exit flow.
            OnCheckForUpdates = async () => { await Task.Delay(500); return false; },
        };

        _aboutWindow = new BrandAboutWindow(options);
        // Distinct, recognizable titles so the capture script can tell the two windows apart even
        // though BrandAboutWindow hides its own title bar (the AppWindow title is still set).
        _aboutWindow.Title = "Window Mode";
        _aboutWindow.Activate();

        var hostedInfo = new AboutInfo
        {
            AppName     = "Brand Test Harness (hosted control)",
            Version     = "0.0.0-dev",
            Description = "Same BrandAboutControl content as the popup, hosted directly inside a plain " +
                          "window with ordinary chrome and no update button — simulating M365Migrator's " +
                          "in-navigation About page, which has no popup or update/exit concept.",
            RepoUrl     = $"{CoreBrand.OrgUrl}/0z0-shared",
            ExternalLibraries = libraries,
        };
        _hostedControlWindow = new HostedControlWindow(hostedInfo);
        _hostedControlWindow.Activate();
    }
}
