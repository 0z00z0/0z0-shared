using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;
using Windows.Graphics;
using ZeroZero.Brand.Core;
// This project's own namespace (ZeroZero.Brand.WinUI) nests inside ZeroZero.Brand, so an
// unqualified "Brand" would resolve to that enclosing namespace segment rather than the
// ZeroZero.Brand.Core.Brand class — alias it to sidestep the collision.
using CoreBrand = ZeroZero.Brand.Core.Brand;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// The shared, parameterized About popup for ZeroZero Software apps — 320px wide, Mica backdrop,
/// centered on the monitor under the cursor, no title bar, always-on-top. Replaces each app's own
/// hand-rolled AboutWindow. Carries its own minimal Win32 P/Invoke (<see cref="NativeMethods"/>)
/// for monitor/DPI metrics, so it has no dependency on a consuming app's own NativeMethods class.
/// </summary>
public sealed partial class BrandAboutWindow : Window
{
    private readonly BrandAboutOptions _options;

    // Cached from ConfigureChrome so ResizeToContent() can recenter on the same monitor
    // without re-querying the cursor position (which may have moved since the window opened).
    private NativeMethods.RECT _workArea;
    private double _scale;

    public BrandAboutWindow(BrandAboutOptions options)
    {
        _options = options;
        InitializeComponent();
        ConfigureChrome();

        var info = options.Info;
        AppNameText.Text     = info.AppName;
        VersionText.Text     = $"v{info.Version}";
        DescriptionText.Text = info.Description;
        FooterText.Text      = $"Copyright © 2026 {CoreBrand.StudioName} · MIT License";

        CloseBtn.Click  += (_, _) => Close();
        GitHubBtn.Click += (_, _) => Open(info.RepoUrl);
        WebsiteBtn.Click += (_, _) => Open(CoreBrand.WebsiteUrl);
        BmacBtn.Click   += (_, _) => Open(CoreBrand.BuyMeACoffeeUrl);

        if (options.OnCheckForUpdates is { } onCheckForUpdates)
        {
            UpdateBtn.Click += (_, _) => _ = onCheckForUpdates();
        }
        else
        {
            // No update channel wired up (e.g. a build with no update service) — hide the button
            // and let "View on GitHub" take the full row instead of leaving a dead gap.
            UpdateBtn.Visibility = Visibility.Collapsed;
            Grid.SetColumnSpan(GitHubBtn, 2);
        }

        PopulateExternalLibraries(info.ExternalLibraries);

        // The window's client height is fixed by ConfigureChrome based on the DESIRED size at
        // construction time (expander collapsed). Without this, opening "External libraries"
        // grows the StackPanel's content past the window's fixed bounds — the credit rows get
        // clipped by the native window frame and are invisible, not just scrolled off (there's no
        // ScrollViewer). Caught by an actual on-screen launch-test; a publish smoke test can't see
        // this because it never renders or interacts with the window.
        // Expander exposes no separate "Collapsed" event — RegisterPropertyChangedCallback on
        // IsExpanded fires for both directions.
        LibrariesExpander.RegisterPropertyChangedCallback(
            Expander.IsExpandedProperty,
            async (_, _) => { await Task.Delay(300); ResizeToContent(); });
    }

    private void PopulateExternalLibraries(IReadOnlyList<ExternalLibrary> libraries)
    {
        if (libraries.Count == 0)
        {
            LibrariesExpander.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var lib in libraries)
        {
            var line = new TextBlock
            {
                FontSize      = 11,
                TextWrapping  = TextWrapping.Wrap,
                Opacity       = 0.85,
            };

            if (lib.Url is { } url)
            {
                var link = new Hyperlink();
                link.Inlines.Add(new Run { Text = lib.Name });
                link.Click += (_, _) => Open(url);
                line.Inlines.Add(link);
                line.Inlines.Add(new Run { Text = $" — {lib.Purpose} ({lib.License})" });
            }
            else
            {
                line.Text = $"{lib.Name} — {lib.Purpose} ({lib.License})";
            }

            LibrariesPanel.Children.Add(line);
        }
    }

    private void ConfigureChrome()
    {
        AppWindow.IsShownInSwitchers = false;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        presenter.IsResizable   = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);

        Root.Width = 320;
        (_workArea, _scale) = NativeMethods.GetCursorMonitorMetrics();

        ResizeToContent();
    }

    /// <summary>
    /// Measures <see cref="Root"/> at its current content (collapsed or expanded) and resizes/
    /// recenters the native window to fit — called once at construction and again whenever the
    /// External-libraries expander toggles, since the window would otherwise stay fixed at its
    /// original (collapsed) height. Recentering on every call keeps growth/shrink symmetric around
    /// the monitor center the window originally opened on, cached in <see cref="_workArea"/> so a
    /// cursor that has since moved to another monitor doesn't shift the window.
    /// </summary>
    private void ResizeToContent()
    {
        Root.Measure(new Size(320, double.PositiveInfinity));
        int cw = (int)Math.Round(320 * _scale);
        int ch = (int)Math.Round((Root.DesiredSize.Height > 0 ? Root.DesiredSize.Height : 270) * _scale);

        // ResizeClient sizes the CLIENT area (not the outer window), so the 320-DIP content fills
        // it exactly — sizing the outer window instead would leave the border eating into the
        // client area and clipping the right-hand buttons. Centre using the resulting outer size.
        AppWindow.ResizeClient(new SizeInt32(cw, ch));
        var outer = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            _workArea.Left + (_workArea.Right  - _workArea.Left - outer.Width)  / 2,
            _workArea.Top  + (_workArea.Bottom - _workArea.Top  - outer.Height) / 2));
    }

    private static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
