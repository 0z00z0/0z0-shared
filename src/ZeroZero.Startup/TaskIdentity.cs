using System.Security.Principal;

namespace ZeroZero.Startup;

/// <summary>Who the task runs as, in the two forms the scheduler wants. The logon trigger takes the
/// account name and the principal takes the security identifier; the scheduler accepts neither in
/// the other's place.</summary>
public readonly record struct TaskIdentity(string AccountName, string Sid)
{
    public static TaskIdentity Current()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier sid = identity.User
            ?? throw new InvalidOperationException("The current Windows identity carries no security identifier.");
        return new TaskIdentity(identity.Name, sid.Value);
    }
}
