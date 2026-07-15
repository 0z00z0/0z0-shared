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
        // Sized close to the control's actual content height (measured on screen) rather than an
        // arbitrary tall default — a real host page would have its own surrounding nav chrome, but
        // an oversized demo window here just reads as a rendering bug (a wall of dead background).
        AppWindow.Resize(new SizeInt32(640, 560));
    }
}
