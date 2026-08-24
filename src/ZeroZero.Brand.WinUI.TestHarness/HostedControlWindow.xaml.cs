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
    public HostedControlWindow(AboutInfo info)
    {
        InitializeComponent();
        Title = "Hosted Control Demo";
        AboutControl.SetInfo(info);

        // The window must fit the control exactly: this demo exists to show the control's real
        // size, so any slack reads as a rendering bug. Measure the control's desired height and
        // size to that, plus a small margin for the title bar and the ScrollViewer's own padding —
        // never a constant, which cannot track the content.
        AboutControl.Measure(new Windows.Foundation.Size(480, double.PositiveInfinity));
        double contentHeight = AboutControl.DesiredSize.Height;

        // Layout measures in device-independent units, but AppWindow.Resize takes physical pixels;
        // the two coincide only at 100% scaling. Scale by the window's own DPI, or a scaled display
        // gets a window far smaller than its content and clips the control.
        double scale = NativeMethods.GetScaleForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        AppWindow.Resize(new SizeInt32(
            (int)Math.Ceiling(640 * scale),
            (int)Math.Ceiling((contentHeight + 96) * scale)));
    }
}
