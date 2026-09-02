using System.Diagnostics;
using Microsoft.Win32;
using ZeroZero.Primitives;

namespace ZeroZero.Lifecycle;

/// <summary>Brings the process back when it exits without being asked to. Armed once, before any
/// window exists; told when an exit is deliberate; on any other clean exit it starts the executable
/// again with <see cref="Relaunch.Argument"/>, unless the session is ending or the limiter has seen
/// its budget already. A crash never arrives here: the runtime raises no exit event for an
/// unhandled exception, so a crash is the crash-dump component's to record and the application's
/// watchdog task's to recover from.</summary>
public sealed class ProcessLifecycle
{
    private readonly ILogSink _log;
    private readonly RelaunchLimiter _limiter;
    private readonly string _executablePath;
    private volatile bool _deliberate;
    private volatile bool _sessionEnding;
    private bool _armed;

    /// <summary>Whether a previous instance's exit hook started this process, rather than a person,
    /// the scheduler or the installer.</summary>
    public bool IsRelaunch { get; }

    public ProcessLifecycle(ProcessLifecycleOptions options, IEnumerable<string>? commandLineArguments = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);

        _log = options.Log;
        _executablePath = options.ExecutablePath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable to relaunch is unknown: no path was given and the process reports none.");
        _limiter = new RelaunchLimiter(options.DataDirectory, _log);
        IsRelaunch = Relaunch.WasRelaunched(commandLineArguments ?? Environment.GetCommandLineArgs().Skip(1));
    }

    /// <summary>Hooks process exit and session ending. Once per process; a second call changes
    /// nothing.</summary>
    public void Arm()
    {
        if (_armed) return;
        _armed = true;

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    /// <summary>The exit about to happen was asked for. What counts as asked for — the tray menu,
    /// the installer, an update — is the application's to decide.</summary>
    public void MarkDeliberateExit() => _deliberate = true;

    internal void NoteSessionEnding() => _sessionEnding = true;

    internal RelaunchDecision DecideOnExit()
    {
        if (_deliberate) return RelaunchDecision.DeliberateExit;
        if (_sessionEnding) return RelaunchDecision.SessionEnding;
        return _limiter.TryRecordRelaunch() ? RelaunchDecision.Relaunch : RelaunchDecision.LimitReached;
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => NoteSessionEnding();

    private void OnProcessExit(object? sender, EventArgs e)
    {
        switch (DecideOnExit())
        {
            case RelaunchDecision.Relaunch:
                Spawn();
                break;
            case RelaunchDecision.DeliberateExit:
                _log.Info("Exit was deliberate; not relaunching.");
                break;
            case RelaunchDecision.SessionEnding:
                _log.Info("The session is ending; not relaunching.");
                break;
            case RelaunchDecision.LimitReached:
                // The limiter has already said why.
                break;
        }
    }

    private void Spawn()
    {
        try
        {
            // The child inherits this process's token, so an elevated process comes back elevated
            // with no prompt; nothing here asks for elevation the parent did not have.
            var start = new ProcessStartInfo(_executablePath, Relaunch.Argument)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? "",
            };
            using Process? child = Process.Start(start);
            _log.Info($"Exit was not asked for; relaunched as process {child?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}.");
        }
        catch (Exception ex)
        {
            _log.Error(nameof(ProcessLifecycle), ex);
        }
    }
}
