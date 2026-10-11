using ZeroZero.Primitives;

namespace ZeroZero.Controls.WinUI;

/// <summary>The framework-free half of <c>SwitcherVisibility</c>, so tests run it without WinUI.</summary>
internal sealed class SwitcherVisibilityGuard
{
    private int _refusalLogged;

    internal bool TrySet(Action<bool> set, bool shown, ILogSink? log)
    {
        try
        {
            set(shown);
            return true;
        }
        // ITaskbarList::HrInit answers E_NOTIMPL while no taskbar window exists.
        catch (NotImplementedException ex)
        {
            // Spent only on a real sink, so a sinkless window cannot use up the application's line.
            if (log is not null && Interlocked.Exchange(ref _refusalLogged, 1) == 0)
                log.Info($"The taskbar does not exist yet, so a window stays as it was in the taskbar and Alt+Tab instead of being {(shown ? "shown there" : "hidden there")}. Windows answered: {ex.Message.ReplaceLineEndings(" ")} Said once per process.");
            return false;
        }
    }
}
