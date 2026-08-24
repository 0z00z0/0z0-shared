using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using ZeroZero.Brand.Core;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Second manual-test scenario: hosts <see cref="BrandAboutControl"/> directly inside a plain
/// window with ordinary title-bar chrome and no "Check for Updates" button — simulating how a full
/// windowed app (e.g. M365Migrator) would embed it in its own in-navigation About page, as opposed
/// to <see cref="BrandAboutWindow"/>'s tray-app popup. Verifies the control renders correctly
/// detached from that window's chrome and update flow.
/// </summary>
public sealed partial class HostedControlWindow : Window
{
    /// <summary>
    /// Client width in device-independent units. The ScrollViewer hands all of it to the control,
    /// whose own MaxWidth then decides the width the content actually lays out at.
    /// </summary>
    private const double ClientWidth = 640;

    public HostedControlWindow(AboutInfo info)
    {
        InitializeComponent();
        Title = "Hosted Control Demo";
        AboutControl.SetInfo(info);

        // The window must fit the control exactly: this demo exists to show the control's real
        // size, so any slack reads as a rendering bug. Measure against the width the control is
        // displayed at — measuring narrower wraps the description into extra lines and reports a
        // height the rendered layout never needs, which shows up as dead space under the content.
        AboutControl.Measure(new Windows.Foundation.Size(ClientWidth, double.PositiveInfinity));
        double contentHeight = AboutControl.DesiredSize.Height;

        // Layout measures in device-independent units, but AppWindow.Resize takes physical pixels;
        // the two coincide only at 100% scaling. Scale by the window's own DPI, or a scaled display
        // gets a window far smaller than its content and clips the control.
        IntPtr window = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        double scale = NativeMethods.GetScaleForWindow(window);

        // Resize sizes the whole window, title bar and borders included, so the chrome has to be
        // added on top of the content. Take it from the window's own frame: a constant allowance
        // is right at one scaling and one theme only, and the surplus becomes empty space.
        SizeInt32 chrome = NativeMethods.GetChromeSizeForWindow(window);
        AppWindow.Resize(new SizeInt32(
            (int)Math.Ceiling(ClientWidth * scale) + chrome.Width,
            (int)Math.Ceiling(contentHeight * scale) + chrome.Height));
    }
}
