using Microsoft.Win32.TaskScheduler;
using ZeroZero.Startup;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace ZeroZero.Startup.Tests;

/// <summary>A task in the scheduler's root folder under a name that says what it is, starting the
/// command interpreter with an exit code of the test's choosing, and deleted when the test ends.
/// Registered as the component builds it where the process is elevated; from a standard token the
/// run level is lowered first, because that one setting is what the scheduler refuses such a
/// token, and every other behaviour is still the real scheduler's.</summary>
internal sealed class DisposableTask : IDisposable
{
    public const string Prefix = "ZeroZero.Startup.Tests.";
    public static readonly string CommandInterpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>A path no file occupies, for a task that cannot start what it points at.</summary>
    public static readonly string MissingExecutable = Path.Combine(Environment.SystemDirectory, "zerozero-startup-tests-no-such-executable.exe");

    /// <summary>Arguments that keep the started program up for about twenty seconds — long enough
    /// to outlast any wait a test uses, short enough that the program is gone soon after. It has to
    /// end by itself: the definition forbids hard termination, so stopping the task returns the
    /// task to ready and leaves the program running.</summary>
    public const string ResidentArguments = "/c ping -n 20 127.0.0.1 >nul";

    public string Name { get; } = Prefix + Guid.NewGuid().ToString("N");
    public RecordingLogSink Log { get; } = new();
    public StartupTaskOptions Options { get; }
    public StartupTask Task { get; }

    public DisposableTask(int exitCode = 0, bool verify = false, string? executablePath = null, string? arguments = null)
    {
        Options = new StartupTaskOptions
        {
            TaskName = Name,
            Description = "Disposable test task. Delete freely.",
            ExecutablePath = executablePath ?? CommandInterpreter,
            Arguments = arguments ?? $"/c exit {exitCode}",
            VerifyByDemandStart = verify,
            Log = Log,
        };
        Task = new StartupTask(Options);
    }

    public void Register()
    {
        using var service = new TaskService();
        TaskIdentity identity = TaskIdentity.Current();
        using TaskDefinition definition = StartupTaskDefinition.Build(service, Options, identity, Options.ExecutablePath!, enabled: true);
        if (!Elevation.IsElevated) definition.Principal.RunLevel = TaskRunLevel.LUA;
        service.RootFolder.RegisterTaskDefinition(Name, definition, TaskCreation.CreateOrUpdate, identity.Sid, null, TaskLogonType.InteractiveToken);
    }

    /// <summary>A reading through a scheduler connection of its own, so what a test asserts was
    /// not answered by the object under test.</summary>
    public T ReadIndependently<T>(Func<ScheduledTask?, T> read)
    {
        using var service = new TaskService();
        using ScheduledTask? task = service.GetTask(Name);
        return read(task);
    }

    /// <summary>What an older build's registration looks like: the same task with the settings the
    /// scheduler defaults to.</summary>
    public void Tamper(Action<TaskDefinition> change)
    {
        using var service = new TaskService();
        using ScheduledTask task = service.GetTask(Name) ?? throw new InvalidOperationException("The task is not registered.");
        change(task.Definition);
        task.RegisterChanges();
    }

    /// <summary>Removes what a run that died left behind.</summary>
    public static void Sweep()
    {
        using var service = new TaskService();
        List<string> leftovers = service.RootFolder.Tasks
            .Where(task => task.Name.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(task => task.Name)
            .ToList();
        foreach (string name in leftovers)
            service.RootFolder.DeleteTask(name, exceptionOnNotExists: false);
    }

    public void Dispose()
    {
        try
        {
            using var service = new TaskService();
            service.RootFolder.DeleteTask(Name, exceptionOnNotExists: false);
        }
        finally
        {
            Task.Dispose();
        }
    }
}
