using System.Diagnostics;

namespace ZeroZero.Diagnostics.Tests;

/// <summary>Runs this test assembly's own executable as a separate process and waits for it to end,
/// killing it if it does not, so a crash that hangs — a dialog, a debugger prompt — fails the test
/// instead of the run.</summary>
public static class ChildProcess
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public sealed record Result(int ExitCode, int ProcessId, string StandardError);

    public static Result Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} did not start.");
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();

        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{executable} {string.Join(' ', arguments)} did not end within {Timeout}.");
        }
        process.WaitForExit();
        Task.WaitAll(standardError, standardOutput);

        return new Result(process.ExitCode, process.Id, standardError.Result);
    }

    /// <summary>The executable built beside the test assembly.</summary>
    public static string OwnExecutable(string assemblyName) =>
        Path.Combine(AppContext.BaseDirectory, assemblyName + ".exe");
}
