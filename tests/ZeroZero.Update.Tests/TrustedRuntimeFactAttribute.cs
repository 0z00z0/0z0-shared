using Xunit;

namespace ZeroZero.Update.Tests;

/// <summary>A test that needs the runtime's own core library to carry a signature this machine
/// trusts — the one file every .NET installation has that exercises the trusted-chain branch.
/// Skipped, and reported as skipped, where it does not.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TrustedRuntimeFactAttribute : FactAttribute
{
    public TrustedRuntimeFactAttribute()
    {
        string runtime = typeof(object).Assembly.Location;
        if (runtime.Length == 0 || !File.Exists(runtime) || AuthenticodeSignature.Check(runtime) != 0)
            Skip = "Needs the runtime's core library to carry a signature under a chain this machine trusts.";
    }
}
