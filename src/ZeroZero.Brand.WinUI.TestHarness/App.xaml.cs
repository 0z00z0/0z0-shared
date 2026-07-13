using Microsoft.UI.Xaml;
using ZeroZero.Brand.Core;
// This project's own namespace nests inside ZeroZero.Brand (same collision documented in
// BrandAboutWindow.xaml.cs), so an unqualified "Brand" resolves to the namespace segment
// instead of ZeroZero.Brand.Core.Brand — alias it to sidestep that.
using CoreBrand = ZeroZero.Brand.Core.Brand;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Interactive manual test rig for <see cref="BrandAboutWindow"/>. Opens the shared About box
/// directly, populated with this repo's own data, so the window can be eyeballed (Mica backdrop,
/// centering, credits expander, link buttons) without building or running ChargeKeeper or
/// HyperVManagerTray. The About window is the app's only window, so closing it ends the process
/// (default <see cref="Application.DispatcherShutdownMode"/> is OnLastWindowClose).
/// </summary>
public partial class App : Application
{
    private Window? _aboutWindow;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
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
                ExternalLibraries =
                [
                    new ExternalLibrary("Microsoft.WindowsAppSDK", "Microsoft", "WinUI 3 / Windows App SDK runtime", "MIT", "https://github.com/microsoft/WindowsAppSDK"),
                    new ExternalLibrary("H.NotifyIcon.WinUI", "HavenDV", "Example third-party credit (not an actual dependency of this harness)", "MIT", "https://github.com/HavenDV/H.NotifyIcon"),
                ],
            },
            // Present so the "Check for Updates" button is visible and clickable for the test —
            // omit this to verify the button hides itself instead (see BrandAboutWindow.xaml.cs).
            // Returns false (no update applied) so the window stays open for inspection rather than
            // driving the new exit flow.
            OnCheckForUpdates = async () => { await Task.Delay(500); return false; },
        };

        _aboutWindow = new BrandAboutWindow(options);
        _aboutWindow.Activate();
    }
}
