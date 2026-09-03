namespace ZeroZero.Config.Watch;

/// <summary>Everything a <see cref="SettingsWatcher{T}"/> needs: which file to watch, how to ask the
/// store what it holds, how to tell it to look again, and what counts as a change worth reporting.
/// </summary>
/// <remarks>The store is reached through two delegates rather than a type, so one watcher serves a
/// whole-file store and a single named section of a shared document alike, and the watcher never
/// needs to know the document's shape.</remarks>
/// <typeparam name="T">The settings shape.</typeparam>
/// <param name="FilePath">The file to watch. Its folder is created if absent, because a folder that
/// does not exist cannot be watched and an absent settings folder is the ordinary first-run state.</param>
/// <param name="Read">The state the store holds now. Called twice per examination, either side of
/// <paramref name="Reload"/>.</param>
/// <param name="Reload">Tells the store to read the file again. Whatever it reports is ignored: the
/// two states either side of it are what decide.</param>
/// <param name="Classifier">Decides whether the difference between those two states matters.</param>
public sealed record SettingsWatcherOptions<T>(
    string FilePath,
    Func<T> Read,
    Action Reload,
    SettingsChangeClassifier<T> Classifier)
    where T : class, new()
{
    /// <summary>How long the file must sit still before it is examined. Defaults to 500 ms, which is
    /// far longer than the few milliseconds a single save's notifications span.</summary>
    public TimeSpan Quiet { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The clock the quiet window is measured on. Defaults to the system clock; a test
    /// passes a controllable one so the collapsing is decided by the code rather than by how busy
    /// the machine is.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Where notifications are raised. Null raises them on the thread the examination ran
    /// on, which is a timer thread; a consumer that touches a user interface passes the context
    /// captured on its own thread.</summary>
    public SynchronizationContext? NotificationContext { get; init; }
}
