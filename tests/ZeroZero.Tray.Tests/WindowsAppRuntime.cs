using System.Runtime.InteropServices;

namespace ZeroZero.Tray.Tests;

/// <summary>Whether the framework package the harness bootstraps against is registered for this
/// user, asked of the package manager the way the bootstrapper asks it, by family name.</summary>
internal static partial class WindowsAppRuntime
{
    /// <summary>The 2.x runtime's family; the family the build kit's Windows App SDK pin resolves to.</summary>
    public const string Family = "Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe";

    /// <summary>The runtime version the kit's pin, 2.2.0, asks for at least.</summary>
    public static readonly Version Minimum = new(2, 2, 0, 0);

    /// <summary>The architecture the harness is built for: the process architecture of the build.</summary>
    public static string Architecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        var other => other.ToString().ToLowerInvariant(),
    };

    public static bool IsRegistered => RegisteredPackages().Any(name =>
        name.Contains($"_{Architecture}_", StringComparison.OrdinalIgnoreCase) && VersionOf(name) is { } version && version >= Minimum);

    /// <summary>The full names of the family's packages registered for the current user.</summary>
    public static IReadOnlyList<string> RegisteredPackages()
    {
        const int ERROR_SUCCESS = 0;
        const int ERROR_INSUFFICIENT_BUFFER = 122;

        uint count = 0, length = 0;
        int result = GetPackagesByPackageFamily(Family, ref count, IntPtr.Zero, ref length, IntPtr.Zero);
        if (result == ERROR_SUCCESS && count == 0) return [];
        if (result != ERROR_INSUFFICIENT_BUFFER) return [];

        IntPtr names = Marshal.AllocHGlobal((int)count * IntPtr.Size);
        IntPtr buffer = Marshal.AllocHGlobal((int)length * sizeof(char));
        try
        {
            result = GetPackagesByPackageFamily(Family, ref count, names, ref length, buffer);
            if (result != ERROR_SUCCESS) return [];

            var found = new List<string>((int)count);
            for (int i = 0; i < count; i++)
            {
                string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size));
                if (name is not null) found.Add(name);
            }
            return found;
        }
        finally
        {
            Marshal.FreeHGlobal(names);
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The version in a package full name: the second underscore-separated part.</summary>
    public static Version? VersionOf(string packageFullName)
    {
        string[] parts = packageFullName.Split('_');
        return parts.Length > 1 && Version.TryParse(parts[1], out var version) ? version : null;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetPackagesByPackageFamily(string packageFamilyName, ref uint count, IntPtr packageFullNames, ref uint bufferLength, IntPtr buffer);
}
