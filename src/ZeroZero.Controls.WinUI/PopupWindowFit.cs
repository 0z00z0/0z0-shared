using Microsoft.UI.Xaml;
using Windows.Graphics;
using ZeroZero.Win32;

namespace ZeroZero.Controls.WinUI;

/// <summary>
/// Sizes a popup window to its own content — grown to fit, capped at a share of the work area, then
/// centred on the monitor it is already on. The first activation arrives before the first layout
/// pass, so the caller asks again when the content takes its real height; this holds the state that
/// makes asking again cheap, resizing and writing a line once per open rather than once per layout
/// pass.
/// </summary>
public sealed class PopupWindowFit
{
    private readonly Window _window;
    private readonly FrameworkElement _content;
    private readonly PopupWindowFitOptions _options;

    private int _fittedWidth;
    private int _fittedHeight;
    private bool _deferralWritten;

    /// <param name="window">The popup being sized.</param>
    /// <param name="content">The scroller's content panel. Its own height, not the scroller's
    /// viewport: the panel's height is independent of the window's and is final after the first
    /// layout pass, so no second pass at the final size is needed.</param>
    /// <param name="options">Width, floor, padding, cap and where to write the line.</param>
    public PopupWindowFit(Window window, FrameworkElement content, PopupWindowFitOptions options)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        _window = window;
        _content = content;
        _options = options;
    }

    /// <summary>Resizes and re-centres the window for the content as it measures now. Does nothing
    /// where the content has not been laid out yet, and nothing where the answer has not moved
    /// since the last call.</summary>
    public void FitToContent()
    {
        // Whether layout has run is asked of the element itself. A measured height of zero is a
        // real answer — a panel whose rows have not been added yet has one — and reading it as
        // "not laid out" leaves such a window at its opening size until something else resizes it.
        double scale = _content.XamlRoot?.RasterizationScale ?? 0;
        if (!_content.IsLoaded || scale <= 0)
        {
            if (!_deferralWritten)
                _options.Log.Info($"{_options.Name} fit deferred: the content is not laid out yet.");
            _deferralWritten = true;
            return;
        }

        double content = _content.ActualHeight + _options.ScrollerPadding.Top + _options.ScrollerPadding.Bottom;

        // A window's native geometry is physical pixels; everything measured above is
        // device-independent units.
        var appWindow = _window.AppWindow;
        PointInt32 position = appWindow.Position;
        SizeInt32 size = appWindow.Size;
        int chrome = size.Height - appWindow.ClientSize.Height;

        // The monitor the window is on now, found from the centre of where it sits. A centre on no
        // monitor answers for the nearest one, so a popup opening off the desktop still lands
        // somewhere reachable.
        var (workArea, _) = MonitorMetrics.ForPoint(position.X + (size.Width / 2),
                                                    position.Y + (size.Height / 2));

        int height = WindowFit.ContentFittedHeight(content, scale, chrome, workArea.Height,
                                                   _options.MinimumHeight, _options.HeightFraction);
        int width = Math.Min(WindowFit.ToPhysicalPixels(_options.Width, scale), workArea.Width);
        if (width == _fittedWidth && height == _fittedHeight) return;

        _fittedWidth = width;
        _fittedHeight = height;

        _options.Log.Info(
            $"{_options.Name} fit: content {content:F0} units at scale {scale}, chrome {chrome} px, " +
            $"cap {WindowFit.HeightCap(workArea.Height, _options.HeightFraction)} px of work area " +
            $"{workArea.Width}x{workArea.Height} px -> {width}x{height} px.");

        appWindow.MoveAndResize(new RectInt32(
            workArea.Left + ((workArea.Width - width) / 2),
            workArea.Top + ((workArea.Height - height) / 2),
            width, height));
    }
}
