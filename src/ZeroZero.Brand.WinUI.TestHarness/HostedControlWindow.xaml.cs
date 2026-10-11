using Microsoft.UI.Xaml;
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

        // The window must fit the control exactly: this demo exists to show its real size, so any
        // slack reads as a rendering bug.
        WindowSizing.FitToContent(this, AboutControl, ClientWidth);
    }
}
