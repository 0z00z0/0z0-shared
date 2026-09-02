using System.Security.Principal;
using Xunit;
using ZeroZero.Startup;

namespace ZeroZero.Startup.Tests;

public class TaskIdentityTests
{
    [Fact]
    public void TheCurrentIdentityCarriesTheAccountNameAndTheSidInTheirOwnPlaces()
    {
        using WindowsIdentity windows = WindowsIdentity.GetCurrent();

        TaskIdentity identity = TaskIdentity.Current();

        Assert.Equal(windows.Name, identity.AccountName);
        Assert.Equal(windows.User!.Value, identity.Sid);
        Assert.StartsWith("S-1-", identity.Sid, StringComparison.Ordinal);
        Assert.DoesNotContain("S-1-", identity.AccountName, StringComparison.Ordinal);
    }
}
