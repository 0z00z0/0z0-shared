using Xunit;

namespace ZeroZero.Tray.Tests;

/// <summary>A fact that starts the WinUI harness, skipped with the reason where the Windows App
/// Runtime the harness is built against is not registered for this user. The runtime's
/// bootstrapper shows a dialog and waits when it finds no match, so a machine without it, a
/// build runner among them, must never start the harness at all.</summary>
public sealed class WindowsAppRuntimeFactAttribute : FactAttribute
{
    public WindowsAppRuntimeFactAttribute()
    {
        if (!WindowsAppRuntime.IsRegistered)
            Skip = $"Needs the Windows App Runtime {WindowsAppRuntime.Minimum} or later for {WindowsAppRuntime.Architecture} registered for this user: the harness cannot start without it.";
    }
}
