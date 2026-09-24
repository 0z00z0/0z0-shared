using Microsoft.UI.Xaml;
using ZeroZero.Primitives;
using ZeroZero.Win32;

namespace ZeroZero.Controls.WinUI;

/// <summary>What the application supplies about a popup sized to its own content. Every measurement
/// is in device-independent units; the conversion to physical pixels happens at the window's own
/// scale.</summary>
public sealed class PopupWindowFitOptions
{
    /// <summary>The window's fixed width.</summary>
    public required int Width { get; init; }

    /// <summary>The height the window never shrinks below, whatever the content measures.</summary>
    public required int MinimumHeight { get; init; }

    /// <summary>The scroller's padding, added to the content panel's own height. The content sits
    /// inside the scroller, so its padding is part of what has to fit.</summary>
    public Thickness ScrollerPadding { get; init; }

    /// <summary>The share of the work area the window may grow to before the content scrolls
    /// instead.</summary>
    public double HeightFraction { get; init; } = WindowFit.DefaultHeightFraction;

    /// <summary>Names the window in the one line written per open.</summary>
    public string Name { get; init; } = "Popup";

    public ILogSink Log { get; init; } = NullLogSink.Instance;
}
