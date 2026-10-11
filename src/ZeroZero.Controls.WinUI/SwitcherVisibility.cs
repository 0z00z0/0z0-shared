using Microsoft.UI.Windowing;
using ZeroZero.Primitives;

namespace ZeroZero.Controls.WinUI;

/// <summary>Sets <see cref="AppWindow.IsShownInSwitchers"/> without a missing taskbar crashing the caller.</summary>
public static class SwitcherVisibility
{
    private static readonly SwitcherVisibilityGuard Guard = new();

    /// <summary>False only where Windows refused for want of a taskbar; the first refusal is logged.</summary>
    public static bool TrySet(AppWindow window, bool shown, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Guard.TrySet(value => window.IsShownInSwitchers = value, shown, log);
    }
}
