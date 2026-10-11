using System.Diagnostics;
using Microsoft.UI.Xaml;
using ZeroZero.Controls.WinUI;
using ZeroZero.Win32;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// The shared, parameterised About popup for ZeroZero Software apps — 320px wide, Mica backdrop,
/// centred on the monitor under the cursor, no title bar, always-on-top.
///
/// This is a thin shell: the actual About content (brand header, links, credits, release notes)
/// lives in the hosted <see cref="BrandAboutControl"/>. This window only owns chrome — sizing,
/// centring, dismissal — plus the tray-app-only "Check for Updates" flow, so a full windowed app
/// (no popup, no update concept) can host <see cref="BrandAboutControl"/> directly instead.
///
/// It dismisses itself the moment it loses focus, except while a transient window of this
/// application is open (see <see cref="TransientWindows"/>).
/// </summary>
public sealed partial class BrandAboutWindow : Window
{
    /// <summary>Client width in device-independent units.</summary>
    private const double ContentWidth = 320;

    private readonly BrandAboutOptions _options;
    private readonly ContentSizedPopup _popup;

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

        // Escape is the same act as looking away, so it takes the same path.
        _popup = ContentSizedPopup.Attach(this, Root, ContentWidth, Dismiss);
        // The credits list and the notes panel change the control's height; only this window's size reacts.
        AboutControl.ContentResized += (_, _) => _popup.Fit();

        CloseBtn.Click += (_, _) => Dismiss();

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
    /// having moved on, and a transient window of this application on screen holds it: an update
    /// question opened from this window's own button would otherwise close the window that opened
    /// it. <see cref="TransientWindows"/> says what is deliberately not re-examined afterwards.
    /// </summary>
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _everActivated = true;
            return;
        }

        if (!_everActivated) return;
        if (TransientWindows.AnyOpen) return;

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

        AboutControl.CancelPendingFetch();
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
}
