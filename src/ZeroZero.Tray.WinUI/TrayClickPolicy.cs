namespace ZeroZero.Tray.WinUI;

/// <summary>
/// Turns the raw mouse messages on the icon into clicks the application acts on. A left click is
/// reported at once rather than after the double-click wait, so a pop-out opens without a pause;
/// the double-click message that may follow is reported as such only when the policy accepted the
/// click before it, within the system's double-click time. The re-open guard: a pop-out that hides
/// on losing focus loses it to the mouse-down of a click on the icon, and the mouse-up that
/// follows would open it again; the application notes the dismissal and that click is dropped.
/// </summary>
/// <remarks>Framework-free, and clocked by the caller, so the timings are pinned by a plain test.</remarks>
public sealed class TrayClickPolicy
{
    private readonly TimeSpan _doubleClickTime;
    private readonly TimeSpan _reopenGuard;
    private TimeSpan? _lastLeft;
    private TimeSpan? _dismissedAt;

    /// <param name="doubleClickTime">The system's double-click time.</param>
    /// <param name="reopenGuard">How long after a dismissal a left click is dropped; the
    /// double-click time when not given.</param>
    public TrayClickPolicy(TimeSpan doubleClickTime, TimeSpan? reopenGuard = null)
    {
        if (doubleClickTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(doubleClickTime), doubleClickTime, "The double-click time is a positive interval.");
        _doubleClickTime = doubleClickTime;
        _reopenGuard = reopenGuard ?? doubleClickTime;
    }

    /// <summary>The application's pop-out was just dismissed, by a click on the icon or otherwise;
    /// a left click within the guard is the same gesture and is dropped.</summary>
    public void NoteDismissed(TimeSpan now) => _dismissedAt = now;

    /// <summary>A left mouse-up on the icon.</summary>
    public TrayClick OnLeftUp(TimeSpan now)
    {
        bool guarded = _dismissedAt is { } dismissed && now >= dismissed && now - dismissed < _reopenGuard;
        _dismissedAt = null;
        if (guarded) return TrayClick.Ignored;

        _lastLeft = now;
        return TrayClick.Left;
    }

    /// <summary>The shell's double-click message. Reported as a double only when the click before
    /// it was accepted within the double-click time; a pair whose first half was dropped is
    /// dropped whole.</summary>
    public TrayClick OnDoubleClick(TimeSpan now)
    {
        if (_lastLeft is { } left && now >= left && now - left <= _doubleClickTime)
        {
            _lastLeft = null;
            return TrayClick.Double;
        }

        return TrayClick.Ignored;
    }
}
