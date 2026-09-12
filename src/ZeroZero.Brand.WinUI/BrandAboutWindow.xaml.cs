using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using ZeroZero.Win32;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// The shared, parameterised About popup for ZeroZero Software apps — 320px wide, Mica backdrop,
/// centred on the monitor under the cursor, no title bar, always-on-top. Takes its monitor and
/// DPI metrics from <see cref="MonitorMetrics"/>, so it has no dependency on a consuming app's
/// own NativeMethods class.
///
/// This is a thin shell: the actual About content (brand header, links, credits, release notes)
/// lives in the hosted <see cref="BrandAboutControl"/>. This window only owns chrome — sizing,
/// centring, dismissal — plus the tray-app-only "Check for Updates" flow, so a full windowed app
/// (no popup, no update concept) can host <see cref="BrandAboutControl"/> directly instead.
///
/// It dismisses itself the moment it loses focus, with no exception for what is on screen at the
/// time: one rule, so nothing has to be explained to whoever is looking at it.
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
    private NativeRect _workArea;
    private double _scale;

    /// <summary>
    /// Set by the first genuine activation. A deactivation arriving before it is the window still
    /// taking focus, not the reader looking elsewhere: without this latch a fast double-click on
    /// whatever opens the window opens it and dismisses it again in the one gesture.
    /// </summary>
    private bool _everActivated;

    /// <summary>
    /// Set once dismissal has begun. Closing deactivates the window, which would otherwise arrive
    /// back here as a second dismissal.
    /// </summary>
    private bool _dismissing;

    public BrandAboutWindow(BrandAboutOptions options)
    {
        _options = options;
        InitializeComponent();

        AboutControl.SetInfo(options.Info);
        // The libraries list and the release-notes panel both live inside the hosted control, but
        // only this window's fixed native size needs to react to them — see ResizeToContent.
        AboutControl.ContentResized += (_, _) => ResizeToContent();

        ConfigureChrome();

        CloseBtn.Click += (_, _) => Dismiss();

        // Escape is the same act as looking away, so it takes the same path rather than a second
        // one of its own. On the root element, so it fires whichever child holds focus.
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) => { args.Handled = true; Dismiss(); };
        Root.KeyboardAccelerators.Add(escape);

        Activated += OnActivated;

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
    /// Losing focus dismisses the window. The first genuine activation arms it, so the deactivation
    /// a window receives while it is still coming up is ignored rather than read as the reader
    /// having moved on.
    /// </summary>
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _everActivated = true;
            return;
        }

        if (!_everActivated) return;

        Dismiss();
    }

    /// <summary>
    /// The single way this window goes away — the close button, Escape, and losing focus all arrive
    /// here. Stops whatever the content has running before closing, so no reply lands on a window
    /// that is gone, and refuses to re-enter: closing deactivates the window, and that deactivation
    /// comes straight back through <see cref="OnActivated"/>.
    /// </summary>
    private void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;

        try { AboutControl.CancelPendingFetch(); }
        catch (Exception ex) { Debug.WriteLine($"BrandAboutWindow: cancelling the notes fetch: {ex}"); }

        Close();
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
            Dismiss();
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
        (_workArea, _scale) = MonitorMetrics.ForCursor();

        ResizeToContent();

        // The pass above runs before the content is in the live visual tree, where a layout is
        // provisional: text measures with the fallback face's metrics rather than the brand face's.
        // Size again once the content is loaded, where the layout is the one on screen; the call
        // above only keeps the window off WinUI's default size in the meantime.
        Root.Loaded += (_, _) => ResizeToContent();
    }

    /// <summary>
    /// Sizes and places the window to fit its content — at construction, once the content loads, and
    /// again whenever the hosted control's libraries list or release-notes panel toggles (via
    /// <see cref="BrandAboutControl.ContentResized"/>), since the window would otherwise stay fixed
    /// at its original height. Recentring on every call keeps growth and shrink symmetric around the
    /// monitor centre the window opened on, cached in <see cref="_workArea"/> so a cursor that has
    /// since moved to another monitor does not shift the window.
    /// </summary>
    private void ResizeToContent()
    {
        // The height comes from the layout the window actually has, not from a measure run ahead of
        // one: a pass taken before the content is arranged answers with the fallback face's metrics
        // and, the first time, with nothing at all — which is what a constant used to stand in for.
        // Rounded up, because a client area a pixel short of its content clips the last row.
        Root.InvalidateMeasure();
        Root.UpdateLayout();
        Root.Measure(new Size(ContentWidth, double.PositiveInfinity));

        // The desired height and not the arranged one: the arranged height is whatever the last
        // resize gave the window, so feeding it back in grows the window a little on every pass.
        int cw = (int)Math.Ceiling(ContentWidth * _scale);
        int ch = (int)Math.Ceiling(Root.DesiredSize.Height * _scale);

        // The client area has to end up exactly the content's size: the content stacks from the top,
        // so any surplus shows as an empty band under the last row. ResizeClient would derive the
        // outer size from a frame that still counts a title bar this presenter does not draw, adding
        // some 52 physical pixels of it at 175% scaling. Add the frame the window actually has,
        // taken from its own rectangles, and size the outer window to that; the client then fills
        // with the 320-DIP content exactly, with no border eating into it.
        var (ncWidth, ncHeight) = MonitorMetrics.NonClientSize(Win32Interop.GetWindowFromWindowId(AppWindow.Id));

        // Nothing in this window scrolls, so whatever falls past the screen's edge is unreachable
        // rather than merely out of sight. Cap the outer size at the work area and let an over-tall
        // window sit against its top, where the rows that survive are the ones read first.
        int workHeight = _workArea.Bottom - _workArea.Top;
        int outerHeight = ch + ncHeight;
        if (workHeight > 0 && outerHeight > workHeight) outerHeight = workHeight;

        AppWindow.Resize(new SizeInt32(cw + ncWidth, outerHeight));
        var outer = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            _workArea.Left + (_workArea.Right - _workArea.Left - outer.Width) / 2,
            _workArea.Top  + Math.Max(0, (workHeight - outer.Height) / 2)));
    }
}
