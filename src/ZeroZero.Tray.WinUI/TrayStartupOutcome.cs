using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ZeroZero.Tray.WinUI;

/// <summary>How TrayStartup.StartAsync ended; docs/zerozero-tray.md says what each leaves behind.</summary>
public enum TrayStartupOutcome
{
    Created,
    ShellNeverAppeared,
    CreationFailed,
}

public readonly record struct TrayStartupResult(
    TrayStartupOutcome Outcome, int Attempts, TimeSpan Elapsed, Exception? LastError)
{
    public bool IsCreated => Outcome == TrayStartupOutcome.Created;

    /// <summary>One plain line for the log saying why the icon was not created, with the last
    /// refusal's message and error code; null for <see cref="TrayStartupOutcome.Created"/>.</summary>
    public string? Explanation => Outcome switch
    {
        TrayStartupOutcome.ShellNeverAppeared =>
            $"Tray icon not created: the taskbar did not appear within {Elapsed.TotalSeconds:0.0} s. The application is expected to try again or be restarted.",
        TrayStartupOutcome.CreationFailed =>
            $"Tray icon not created: the notification area refused it {Attempts} times in {Elapsed.TotalSeconds:0.0} s, most likely because it was not ready yet. The application is expected to try again or be restarted. Last refusal: {LastError?.Message} ({ErrorCode(LastError)}).",
        _ => null,
    };

    // The notify-icon library calls the shell without capturing the Win32 error, so its refusal has no code.
    private static string ErrorCode(Exception? ex) => ex switch
    {
        Win32Exception win32 => $"Win32 error {win32.NativeErrorCode}",
        ExternalException external => $"HRESULT 0x{external.HResult:X8}",
        _ => "no error code given",
    };
}
