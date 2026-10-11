using System.Diagnostics;

namespace ZeroZero.Tray.WinUI;

/// <summary>Starts a <see cref="TrayHost"/> once the taskbar exists, retrying a refused icon within
/// a bound, and reports the outcome instead of throwing; docs/zerozero-tray.md has the wiring.</summary>
public static class TrayStartup
{
    public static Task<TrayStartupResult> StartAsync(TrayHost host, TrayStartupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        return RunAsync(options, host.Prepare, host.CreateIcon, host.Abandon);
    }

    /// <summary>Starts a tray icon from plain callbacks, for a host that cannot derive from
    /// <see cref="TrayHost"/>. There is no prepare step: a caller with its own preparation work
    /// does it inside <paramref name="create"/>.</summary>
    public static Task<TrayStartupResult> StartAsync(Action create, Action abandon, TrayStartupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(abandon);
        return RunAsync(options, static () => { }, create, abandon);
    }

    private static Task<TrayStartupResult> RunAsync(TrayStartupOptions? options, Action prepare, Action create, Action abandon)
    {
        options ??= new TrayStartupOptions();
        options.Validate();
        UiThread.Dispatcher();

        var clock = Stopwatch.StartNew();
        return TrayStartupSequence.RunAsync(
            options,
            () => NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero,
            prepare,
            create,
            abandon,
            wait => Task.Delay(wait),
            () => clock.Elapsed);
    }
}
