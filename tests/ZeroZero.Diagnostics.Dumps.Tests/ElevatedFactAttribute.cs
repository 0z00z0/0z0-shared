using System.Security.Principal;
using Xunit;

namespace ZeroZero.Diagnostics.Dumps.Tests;

/// <summary>A fact that needs an elevated process, skipped with the reason where it has none.
/// Windows Error Reporting reads local dump registrations from the machine hive alone, so the one
/// test that provokes a real dump can only run where that hive is writable.</summary>
public sealed class ElevatedFactAttribute : FactAttribute
{
    public ElevatedFactAttribute()
    {
        if (!IsElevated)
            Skip = "Needs an elevated process: Windows Error Reporting reads local dump registrations from the machine hive only.";
    }

    private static bool IsElevated
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
