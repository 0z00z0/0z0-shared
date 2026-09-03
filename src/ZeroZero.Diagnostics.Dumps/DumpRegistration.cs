using Microsoft.Win32;
using ZeroZero.Primitives;

namespace ZeroZero.Diagnostics.Dumps;

/// <summary>Windows Error Reporting's local dump registration for one executable, treated as the
/// state it is: created when armed, removed when disarmed, and the shared root removed once it is
/// empty, because the root's mere existence turns dump collection on for every process under it.</summary>
/// <remarks>The hive is the application's. Windows Error Reporting documents the local machine hive
/// alone, and a registration under the current user's hive produces no dump (measured), so an
/// application that is not elevated has no dump to arm. Disarming and sweeping write that hive too,
/// so an unelevated process cannot do those either — removing what an elevated run left is itself a
/// machine-hive write. Nothing here reaches for a settings store — the armed flag arrives as a
/// parameter, so a whole-file settings save cannot clobber it. A registry refusal is thrown, not
/// hidden: an unelevated process told to write the machine hive should hear about it.</remarks>
public sealed class DumpRegistration
{
    /// <summary>Where Windows Error Reporting reads local dump settings, relative to the hive root.</summary>
    public const string LocalDumpsPath = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps";

    private const string DumpFolderValue = "DumpFolder";
    private const string DumpCountValue = "DumpCount";
    private const string DumpTypeValue = "DumpType";

    private readonly RegistryKey _hive;
    private readonly string _rootPath;
    private readonly ILogSink _log;

    public DumpRegistration(RegistryKey hive, ILogSink log) : this(hive, LocalDumpsPath, log) { }

    /// <summary>The tests register under a scratch key rather than the real root.</summary>
    internal DumpRegistration(RegistryKey hive, string rootPath, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(log);
        _hive = hive;
        _rootPath = rootPath;
        _log = log;
    }

    /// <summary>Arms or disarms according to the flag the application holds.</summary>
    public void Apply(DumpPolicy policy, bool armed)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (armed) Arm(policy);
        else Disarm(policy.ExecutableName);
    }

    /// <summary>Writes the registration, replacing whatever the key held.</summary>
    public void Arm(DumpPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using RegistryKey key = _hive.CreateSubKey(KeyPath(policy.ExecutableName), writable: true);
        key.SetValue(DumpFolderValue, policy.DumpDirectory, RegistryValueKind.ExpandString);
        key.SetValue(DumpCountValue, policy.RetainedCount, RegistryValueKind.DWord);
        key.SetValue(DumpTypeValue, (int)policy.DumpType, RegistryValueKind.DWord);

        _log.Info($"Crash dumps armed for {policy.ExecutableName}: {policy.DumpType} dumps, {policy.RetainedCount} retained, in {policy.DumpDirectory}.");
    }

    /// <summary>Removes the registration, and the root with it when nothing else is left there.</summary>
    public void Disarm(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        if (Remove(executableName))
            _log.Info($"Crash dumps disarmed for {executableName}.");
        RemoveRootIfEmpty();
    }

    public bool IsArmed(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        using RegistryKey? key = _hive.OpenSubKey(KeyPath(executableName));
        return key is not null;
    }

    /// <summary>The registration as it stands, or null where there is none or it is incomplete.</summary>
    public DumpPolicy? Read(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        using RegistryKey? key = _hive.OpenSubKey(KeyPath(executableName));
        if (key is null) return null;

        // Unexpanded: the value is stored as written, and Windows Error Reporting expands it at
        // crash time under its own environment, not this process's.
        if (key.GetValue(DumpFolderValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string folder
            || key.GetValue(DumpCountValue) is not int count
            || key.GetValue(DumpTypeValue) is not int type
            || !Enum.IsDefined((DumpType)type)
            || count < 1)
            return null;

        return new DumpPolicy(executableName, folder, (DumpType)type, count);
    }

    /// <summary>Removes the registrations older builds left under other executable names. The names
    /// are the application's history and arrive as parameters. Returns how many were there.</summary>
    public int RemoveResidue(params IEnumerable<string> legacyExecutableNames)
    {
        ArgumentNullException.ThrowIfNull(legacyExecutableNames);

        int removed = 0;
        foreach (string name in legacyExecutableNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!Remove(name)) continue;
            removed++;
            _log.Info($"Removed the crash dump registration an older build left for {name}.");
        }
        RemoveRootIfEmpty();
        return removed;
    }

    /// <summary>Removes the shared root when it holds no registration and no value. True when it was removed.</summary>
    public bool RemoveRootIfEmpty()
    {
        using (RegistryKey? root = _hive.OpenSubKey(_rootPath))
        {
            if (root is null) return false;
            if (root.SubKeyCount > 0 || root.ValueCount > 0) return false;
        }

        _hive.DeleteSubKey(_rootPath, throwOnMissingSubKey: false);
        _log.Info("Removed the empty local dumps root; its presence alone enables collection for every process.");
        return true;
    }

    private bool Remove(string executableName)
    {
        if (!IsArmed(executableName)) return false;
        _hive.DeleteSubKeyTree(KeyPath(executableName), throwOnMissingSubKey: false);
        return true;
    }

    private string KeyPath(string executableName) => _rootPath + "\\" + executableName;
}
