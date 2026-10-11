using Microsoft.UI.Dispatching;

namespace ZeroZero.Tray.WinUI;

internal static class UiThread
{
    /// <summary>The calling thread's dispatcher queue; throws off the UI thread.</summary>
    internal static DispatcherQueue Dispatcher() => DispatcherQueue.GetForCurrentThread()
        ?? throw new InvalidOperationException("Start the host on the UI thread: its listeners are marshalled to that thread's dispatcher queue.");
}
