namespace ZeroZero.Win32;

/// <summary>
/// How many of the application's own short-lived windows are on screen. A window that dismisses
/// itself the moment it loses focus asks <see cref="AnyOpen"/> before it does, because one of its
/// own application's windows taking focus is not the reader looking elsewhere.
/// </summary>
/// <remarks>
/// Without this, a window opened on top of a self-dismissing one closes the window beneath in the
/// same gesture: the update question appears, the About window behind it reads the lost focus as a
/// dismissal, and the reader is left with half of what they asked for.
/// <para>
/// A window that was deactivated while a transient was open is not re-examined when the last one
/// closes, so it stays open until the reader looks away again. That is deliberate: a window
/// outstaying its welcome by one glance is a smaller fault than one vanishing mid-update.
/// </para>
/// <para>
/// The count is process-wide and thread-safe. Every scope is disposed exactly once, however many
/// times <see cref="IDisposable.Dispose"/> is called on it.
/// </para>
/// </remarks>
public static class TransientWindows
{
    private static int _open;

    /// <summary>Whether any transient window of this application is on screen.</summary>
    public static bool AnyOpen => Volatile.Read(ref _open) > 0;

    /// <summary>Counts one transient window as open until the returned scope is disposed.</summary>
    public static IDisposable Enter()
    {
        Interlocked.Increment(ref _open);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private int _left;

        public void Dispose()
        {
            // A window can be closed more than once — the close button and the Closed handler both
            // arrive here — and a count that went negative would disarm every self-dismissing
            // window for the life of the process.
            if (Interlocked.Exchange(ref _left, 1) == 0) Interlocked.Decrement(ref _open);
        }
    }
}
