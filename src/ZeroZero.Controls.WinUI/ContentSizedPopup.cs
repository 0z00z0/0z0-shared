using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using ZeroZero.Win32;

namespace ZeroZero.Controls.WinUI;

/// <summary>A fixed-width popup sized to content that does not scroll: off the taskbar and Alt+Tab, frameless,
/// always on top, centred on the monitor under the cursor, Escape handled from any control in it.
/// Content that scrolls takes <see cref="PopupWindowFit"/> instead.</summary>
public sealed class ContentSizedPopup
{
    private readonly Window _window;
    private readonly FrameworkElement _content;
    private readonly double _contentWidth;

    // Taken once, so a refit stays on the monitor the window opened on.
    private readonly NativeRect _workArea;
    private readonly double _scale;

    private bool _closed;

    private ContentSizedPopup(Window window, FrameworkElement content, double contentWidth)
    {
        _window = window;
        _content = content;
        _contentWidth = contentWidth;
        (_workArea, _scale) = MonitorMetrics.ForCursor();
    }

    /// <summary>Called from the window's constructor after <c>InitializeComponent</c>, with the root
    /// element and its width in device-independent units. Fits now and again when the root loads.</summary>
    public static ContentSizedPopup Attach(Window window, FrameworkElement content, double contentWidth, Action escape)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(escape);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentWidth);

        SwitcherVisibility.TrySet(window.AppWindow, shown: false);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        window.AppWindow.SetPresenter(presenter);

        content.Width = contentWidth;

        var popup = new ContentSizedPopup(window, content, contentWidth);
        window.Closed += (_, _) => popup._closed = true;

        var accelerator = new KeyboardAccelerator { Key = VirtualKey.Escape };
        accelerator.Invoked += (_, args) => { args.Handled = true; escape(); };
        content.KeyboardAccelerators.Add(accelerator);

        // Provisional: before Loaded, text measures with the fallback face's metrics.
        popup.Fit();
        content.Loaded += (_, _) => popup.Fit();
        return popup;
    }

    /// <summary>Resizes the window to the content as it measures now and recentres it. A measure of
    /// zero counts as 200 units. Does nothing once the window has closed.</summary>
    public void Fit()
    {
        if (_closed) return;

        // Desired, not arranged: the arranged height is the last resize's, and feeding it back grows the window.
        _content.InvalidateMeasure();
        _content.UpdateLayout();
        _content.Measure(new Size(_contentWidth, double.PositiveInfinity));

        // The frame read from the window: ResizeClient counts a title bar this presenter does not draw.
        var (frameWidth, frameHeight) = MonitorMetrics.NonClientSize(Win32Interop.GetWindowFromWindowId(_window.AppWindow.Id));
        var (width, height) = ContentSizedPopupLayout.OuterSize(
            _contentWidth, _content.DesiredSize.Height, _scale, frameWidth, frameHeight, _workArea.Height);

        _window.AppWindow.Resize(new SizeInt32(width, height));
        var outer = _window.AppWindow.Size;
        var (x, y) = ContentSizedPopupLayout.Origin(
            _workArea.Left, _workArea.Top, _workArea.Width, _workArea.Height, outer.Width, outer.Height);
        _window.AppWindow.Move(new PointInt32(x, y));
    }
}
