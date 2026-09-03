using System.ComponentModel;
using System.Diagnostics;

namespace ZeroZero.Update;

/// <summary>Starts the installer. The shell in the application; a recorder in a test, where no
/// installer ever runs.</summary>
public interface IInstallerLauncher
{
    /// <exception cref="Win32Exception">The process could not be started.</exception>
    void Start(string path, string arguments);
}

/// <summary>Starts the file through the shell, so its own manifest decides its execution level:
/// a per-user installer starts with no prompt, and one that needs elevation asks for it itself.</summary>
public sealed class ShellInstallerLauncher : IInstallerLauncher
{
    public void Start(string path, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var start = new ProcessStartInfo(path, arguments ?? "")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? "",
        };
        using Process? process = Process.Start(start);
        if (process is null) throw new Win32Exception("The installer process was not started.");
    }
}
