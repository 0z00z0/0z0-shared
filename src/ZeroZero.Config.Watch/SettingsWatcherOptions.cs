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
    /// <summary>What the store says stands between it and the file, or null when nothing does.
    /// Asked once per examination.</summary>
    /// <remarks>
    /// <para>A store can read a file successfully and still see nothing in it. A section addressed
    /// as <c>general</c> against a document spelling it <c>General</c> is the measured case: the
    /// section reads as its type's defaults for ever, every write to it is refused, and a re-read
    /// therefore moves nothing however the file is edited. Both states either side of the re-read
    /// are equal, so the examination is honestly not a change — and a person editing that file
    /// would otherwise watch nothing happen and be told nothing.</para>
    /// <para>What comes back is reported on <see cref="SettingsChangeEventArgs{T}.Obstruction"/>
    /// and, once per distinct reason, through <see cref="SettingsWatcher{T}.Failed"/>. A store with
    /// nothing in its way leaves this null, which is what a whole-file store always does; the
    /// wording is the store's, because only the store knows what it is looking at.</para>
    /// </remarks>
    public Func<string?>? Obstruction { get; init; }

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
