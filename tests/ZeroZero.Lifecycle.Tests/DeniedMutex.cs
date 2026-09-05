using System.Runtime.InteropServices;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>A named mutex nobody may open: created through the platform call with an empty
/// protected discretionary list, which grants no access to anyone at all. It stands in for the case
/// a test cannot otherwise reach — a name held under rights this process does not have, which on a
/// real machine is another session's instance or one running elevated.</summary>
internal sealed partial class DeniedMutex : IDisposable
{
    private const uint SddlRevision1 = 1;

    private IntPtr _handle;

    public DeniedMutex(string name)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P", SddlRevision1, out IntPtr descriptor, out _))
            throw new InvalidOperationException($"The security descriptor would not parse: {Marshal.GetLastWin32Error()}.");

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = 0,
            };

            _handle = CreateMutex(ref attributes, initialOwner: false, name);
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException($"The mutex was not created: {Marshal.GetLastWin32Error()}.");
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateMutexW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateMutex(ref SecurityAttributes attributes, [MarshalAs(UnmanagedType.Bool)] bool initialOwner, string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }
}
