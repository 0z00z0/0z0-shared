using System.Security.Principal;
using Xunit;

namespace ZeroZero.Startup.Tests;

internal static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}

/// <summary>A test the scheduler only lets an elevated process perform: registering a task that
/// runs at the highest level. Skipped, and reported as skipped, from a standard token.</summary>
public sealed class ElevatedFactAttribute : FactAttribute
{
    public ElevatedFactAttribute()
    {
        if (!Elevation.IsElevated)
            Skip = "Needs an elevated process: the scheduler refuses a highest-run-level task from a standard token.";
    }
}

/// <summary>A test of what a standard token is refused. Meaningless elevated, and skipped there.</summary>
public sealed class UnelevatedFactAttribute : FactAttribute
{
    public UnelevatedFactAttribute()
    {
        if (Elevation.IsElevated)
            Skip = "Measures the refusal a standard token gets; the process is elevated.";
    }
}
