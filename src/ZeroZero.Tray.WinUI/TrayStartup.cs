using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace ZeroZero.Tray.WinUI;

/// <summary>Starts a <see cref="TrayHost"/> once the taskbar exists, retrying a refused icon within
/// a bound, and reports the outcome instead of throwing; docs/zerozero-tray.md has the wiring.</summary>
public static class TrayStartup
{
    public static Task<TrayStartupResult> StartAsync(TrayHost host, TrayStartupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        options ??= new TrayStartupOptions();
        options.Validate();
        if (DispatcherQueue.GetForCurrentThread() is null)
            throw new InvalidOperationException("Start the host on the UI thread: its listeners are marshalled to that thread's dispatcher queue.");

        var clock = Stopwatch.StartNew();
        return TrayStartupSequence.RunAsync(
            options,
            () => NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero,
            host.Prepare,
            host.CreateIcon,
            host.Abandon,
            wait => Task.Delay(wait),
            () => clock.Elapsed);
    }
}
