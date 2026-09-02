using ZeroZero.Win32;

namespace ZeroZero.Tray;

/// <summary>
/// The size the taskbar draws a notification icon at, in physical pixels: 16 device-independent
/// units, scaled by the taskbar's own display. Under per-monitor DPI awareness the process's scale
/// is whichever monitor its last window was on, which is not where the icon is drawn; an icon
/// rendered at the wrong scale is resampled by the shell and comes out soft.
/// </summary>
public static class TrayIconSlot
{
    /// <summary>The slot at 100 %.</summary>
    public const int BaselinePixels = 16;

    /// <summary>The slot at a scale: 20 at 125 %, 24 at 150 %, 28 at 175 %, 32 at 200 %.</summary>
    public static int PixelsFor(double scale)
    {
        if (double.IsNaN(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "A display scale is a positive factor, 1.0 at 100 %.");

        return (int)Math.Round(BaselinePixels * scale, MidpointRounding.AwayFromZero);
    }

    /// <summary>The slot at the taskbar's scale, read from the taskbar window itself.</summary>
    public static int PixelsForTaskbar() => PixelsFor(MonitorMetrics.ScaleForTaskbar());
}
