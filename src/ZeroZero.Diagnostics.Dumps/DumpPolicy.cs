namespace ZeroZero.Diagnostics.Dumps;

/// <summary>What one executable's dump registration says. Every value is the application's; there is
/// no constructor with fewer parameters, so no default can stand in for a decision.</summary>
public sealed record DumpPolicy
{
    public DumpPolicy(string executableName, string dumpDirectory, DumpType dumpType, int retainedCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        if (executableName.AsSpan().IndexOfAny('\\', '/') >= 0)
            throw new ArgumentException("The executable name is the image file name Windows Error Reporting matches, not a path.", nameof(executableName));

        ArgumentException.ThrowIfNullOrWhiteSpace(dumpDirectory);
        // Windows Error Reporting expands the value, so %LOCALAPPDATA%\... is as valid as a full path;
        // a relative one would resolve against whatever directory WerFault happens to run in.
        if (!Path.IsPathRooted(dumpDirectory) && !dumpDirectory.StartsWith('%'))
            throw new ArgumentException("The dump directory must be a full path or start with an environment variable.", nameof(dumpDirectory));

        if (!Enum.IsDefined(dumpType))
            throw new ArgumentOutOfRangeException(nameof(dumpType), dumpType, "Dump type is Mini or Full.");

        ArgumentOutOfRangeException.ThrowIfLessThan(retainedCount, 1);

        ExecutableName = executableName;
        DumpDirectory = dumpDirectory;
        DumpType = dumpType;
        RetainedCount = retainedCount;
    }

    /// <summary>The image file name, extension included — <c>MyApp.exe</c> — as the registration is keyed.</summary>
    public string ExecutableName { get; }

    /// <summary>Where the dumps go: a full path, or one starting with an environment variable.</summary>
    public string DumpDirectory { get; }

    public DumpType DumpType { get; }

    /// <summary>How many dumps Windows Error Reporting keeps before it starts replacing the oldest.</summary>
    public int RetainedCount { get; }
}
