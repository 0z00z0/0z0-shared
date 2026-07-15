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

        // Measure the control's real desired height and size the window to fit it (plus a small
        // margin for the title bar and the ScrollViewer's own padding) instead of a guessed
        // constant — a mismatched constant left a wall of dead background below the content, which
        // reads as a rendering bug in a demo whose whole point is showing the control's real size.
        AboutControl.Measure(new Windows.Foundation.Size(480, double.PositiveInfinity));
        int contentHeight = (int)Math.Ceiling(AboutControl.DesiredSize.Height);
        AppWindow.Resize(new SizeInt32(640, contentHeight + 96));
    }
}
