using Microsoft.Win32;

namespace ZeroZero.Diagnostics.Dumps.Tests;

/// <summary>A key of the test's own under the current user's hive, standing in for a hive root, so
/// the registration writes a real registry without touching where Windows Error Reporting reads.</summary>
public sealed class ScratchHive : IDisposable
{
    private const string TestsPath = @"Software\ZeroZero.Diagnostics.Dumps.Tests";

    private readonly string _name = Guid.NewGuid().ToString("N");

    public ScratchHive() => Root = Registry.CurrentUser.CreateSubKey(TestsPath + "\\" + _name, writable: true);

    /// <summary>The key the registration treats as the hive root.</summary>
    public RegistryKey Root { get; }

    /// <summary>The local-dumps path relative to the root, kept short under the scratch key.</summary>
    public const string LocalDumps = "LocalDumps";

    public RegistryKey? OpenLocalDumps() => Root.OpenSubKey(LocalDumps);

    public RegistryKey? Open(string executableName) => Root.OpenSubKey(LocalDumps + "\\" + executableName);

    public void Dispose()
    {
        Root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(TestsPath + "\\" + _name, throwOnMissingSubKey: false);
        using RegistryKey? tests = Registry.CurrentUser.OpenSubKey(TestsPath);
        if (tests is { SubKeyCount: 0, ValueCount: 0 })
            Registry.CurrentUser.DeleteSubKey(TestsPath, throwOnMissingSubKey: false);
    }
}
