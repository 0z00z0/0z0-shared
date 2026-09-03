using System.Diagnostics;
using System.Reflection;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>Runs the release scripts under .github/scripts through pwsh and captures what they said.</summary>
internal static class Scripts
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Directory => Path.Combine(RepoRoot, ".github", "scripts");

    /// <summary>The configuration this assembly was built in, so a pack with --no-build finds the same build.</summary>
    public static string Configuration { get; } =
        typeof(Scripts).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";

    public static ScriptResult Run(string script, IReadOnlyDictionary<string, string?>? environment, params string[] arguments)
    {
        var start = Start();
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(Directory, script));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Execute(start, environment);
    }

    /// <summary>Runs a command after dot-sourcing manifest.ps1, for the functions it defines.</summary>
    public static ScriptResult Manifest(string command) =>
        Command($". {Quote(Path.Combine(Directory, "manifest.ps1"))}; {command}");

    public static ScriptResult Command(string command)
    {
        var start = Start();
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        return Execute(start, null);
    }

    /// <summary>A PowerShell single-quoted literal.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static ProcessStartInfo Start()
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }

    private static ScriptResult Execute(ProcessStartInfo start, IReadOnlyDictionary<string, string?>? environment)
    {
        // On a runner GITHUB_SHA would stand in for a missing -Commit and GITHUB_OUTPUT would be
        // appended to; the tests pass the commit they mean and want no side effect on the run.
        start.Environment.Remove("GITHUB_SHA");
        start.Environment.Remove("GITHUB_OUTPUT");
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null) start.Environment.Remove(name);
                else start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("pwsh did not finish within five minutes.");
        }
        return new ScriptResult(process.ExitCode, output.Result + error.Result);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "0z0-shared.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("0z0-shared.slnx was not found above the test assembly.");
    }
}

public sealed record ScriptResult(int ExitCode, string Output)
{
    public bool Passed => ExitCode == 0;

    public override string ToString() => $"exit code {ExitCode}{Environment.NewLine}{Output}";
}
