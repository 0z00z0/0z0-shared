using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// The shared, parameterized About popup for ZeroZero Software apps — 320px wide, Mica backdrop,
/// centred on the monitor under the cursor, no title bar, always-on-top. Carries its own minimal
/// Win32 P/Invoke (<see cref="NativeMethods"/>) for monitor/DPI metrics, so it has no dependency
/// on a consuming app's own NativeMethods class.
///
/// This is a thin shell: the actual About content (brand header, links, credits) lives in the
/// hosted <see cref="BrandAboutControl"/>. This window only owns chrome — sizing, centring,
/// close — plus the tray-app-only "Check for Updates" flow, so a full windowed app (no popup, no
/// update concept) can host <see cref="BrandAboutControl"/> directly instead of this window.
/// </summary>
public sealed partial class BrandAboutWindow : Window
{
    /// <summary>
    /// Client width in device-independent units. The content is both measured against and laid out
    /// at this width, so the measured height is the height that renders.
    /// </summary>
    private const double ContentWidth = 320;

    private readonly BrandAboutOptions _options;

    // Cached from ConfigureChrome so ResizeToContent() can recentre on the same monitor
    // without re-querying the cursor position (which may have moved since the window opened).
    private NativeMethods.RECT _workArea;
    private double _scale;

    public BrandAboutWindow(BrandAboutOptions options)
    {
        _options = options;
        InitializeComponent();

        AboutControl.SetInfo(options.Info);
        // The libraries expander lives inside the hosted control, but only this window's fixed
        // native size needs to react to it — see ResizeToContent's doc for why.
        AboutControl.ContentResized += (_, _) => ResizeToContent();

        ConfigureChrome();

        CloseBtn.Click += (_, _) => Close();

        if (options.OnCheckForUpdates is { } onCheckForUpdates)
        {
            UpdateBtn.Click += async (_, _) => await RunUpdateCheckAsync(onCheckForUpdates);
        }
        else
        {
            // No update channel wired up (e.g. a build with no update service) — hide the button
            // entirely rather than leaving a dead, disabled row.
            UpdateBtn.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Runs the host's update check and, if it reports an update was applied, owns the clean exit:
    /// gives the host a chance to tear down via <see cref="BrandAboutOptions.OnBeforeExit"/> (which
    /// may veto), then closes the window so the app can terminate for the installer to relaunch it.
    /// Guards the async-void click handler so a throwing host callback can't take the app down.
    /// </summary>
    private async Task RunUpdateCheckAsync(Func<Task<bool>> onCheckForUpdates)
    {
        UpdateBtn.IsEnabled = false;
        bool exiting = false;
        try
        {
            if (!await onCheckForUpdates())
                return;   // no update applied — leave the window open

            if (_options.OnBeforeExit is { } onBeforeExit && !await onBeforeExit())
                return;   // host vetoed the exit — leave the window open

            exiting = true;
            Close();
        }
        catch (Exception ex)
        {
            // The host owns update-flow error reporting; keep a thrown callback from crashing the
            // app through the async-void handler, and leave the window open to try again.
            Debug.WriteLine($"BrandAboutWindow: update check failed: {ex}");
        }
        finally
        {
            // The awaits above yield; the user may have closed this always-on-top window meanwhile,
            // so touching UpdateBtn can throw RO_E_CLOSED. Re-enabling a closed window is moot — guard
            // it so nothing escapes the async-void click handler onto the UI thread.
            if (!exiting)
            {
                try { UpdateBtn.IsEnabled = true; }
                catch (Exception ex) { Debug.WriteLine($"BrandAboutWindow: re-enable after close: {ex}"); }
            }
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

        Root.Width = ContentWidth;
        (_workArea, _scale) = NativeMethods.GetCursorMonitorMetrics();

        ResizeToContent();

        // A measure taken here reports a provisional layout: the content is not in the live visual
        // tree yet, so its text is measured with fallback metrics rather than the brand font's, and
        // comes out taller than what renders. Size again once the content is loaded, where the
        // measure is the layout on screen; the call above only keeps the window off WinUI's default
        // size in the meantime.
        Root.Loaded += (_, _) => ResizeToContent();
    }

    /// <summary>
    /// Measures <see cref="Root"/> at its current content (libraries collapsed or expanded) and
    /// resizes/recentres the native window to fit — called at construction, once the content loads,
    /// and again whenever the hosted control's external-libraries expander toggles (via
    /// <see cref="BrandAboutControl.ContentResized"/>), since the window would otherwise stay fixed
    /// at its original (collapsed) height. Recentring on every call keeps growth/shrink symmetric
    /// around the monitor centre the window originally opened on, cached in <see cref="_workArea"/>
    /// so a cursor that has since moved to another monitor doesn't shift the window.
    /// </summary>
    private void ResizeToContent()
    {
        Root.Measure(new Size(ContentWidth, double.PositiveInfinity));
        int cw = (int)Math.Round(ContentWidth * _scale);
        int ch = (int)Math.Round((Root.DesiredSize.Height > 0 ? Root.DesiredSize.Height : 270) * _scale);

        // The client area has to end up exactly the content's size: the content stacks from the top,
        // so any surplus shows as an empty band under the last row. ResizeClient would derive the
        // outer size from a frame that still counts a title bar this presenter does not draw, adding
        // some 52 physical pixels of it at 175% scaling. Add the frame the window actually has,
        // taken from its own rectangles, and size the outer window to that; the client then fills
        // with the 320-DIP content exactly, with no border eating into it. Centre using that same
        // outer size.
        var (ncWidth, ncHeight) = NativeMethods.GetNonClientSize(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        AppWindow.Resize(new SizeInt32(cw + ncWidth, ch + ncHeight));
        var outer = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            _workArea.Left + (_workArea.Right  - _workArea.Left - outer.Width)  / 2,
            _workArea.Top  + (_workArea.Bottom - _workArea.Top  - outer.Height) / 2));
    }
}
