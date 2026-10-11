namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>The rig's command line. Read from the process, because an unpackaged WinUI launch
/// carries no arguments on the activation event.</summary>
internal static class HarnessArguments
{
    /// <summary>Whether the flag is present, compared ordinally.</summary>
    public static bool Has(string[] commandLine, string flag) => Array.IndexOf(commandLine, flag) >= 0;

    /// <summary>The argument after <paramref name="option"/>; null when the option is absent or last.</summary>
    public static string? ValueAfter(string[] commandLine, string option)
    {
        int index = Array.IndexOf(commandLine, option);
        return index >= 0 && index + 1 < commandLine.Length ? commandLine[index + 1] : null;
    }
}
