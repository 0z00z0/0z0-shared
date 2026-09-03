namespace ZeroZero.Config.Watch;

/// <summary>Watches one settings file and reports an edit made outside the application.</summary>
/// <remarks>
/// <para>A save reaches the operating system as several notifications — measured: an in-place write
/// arrives as two, and the atomic write this library performs arrives as a delete followed by a
/// rename, with no change notification on the target at all. Every one of them is a signal, and a
/// trailing quiet window collapses the burst into one examination.</para>
/// <para>An examination asks the store what it holds, tells it to read the file again, asks a second
/// time, and hands both states to the classifier. <b>That is also how the application's own writes
/// stay quiet.</b> A write the store made leaves the file agreeing with what the store already
/// holds, so re-reading moves nothing and the two states are equal — no pause around the write, no
/// record of what was last written, and nothing that a slow notification or a busy machine can get
/// wrong. An edit made by hand leaves the file disagreeing, and is reported.</para>
/// <para><see cref="Examined"/> is raised after every examination and <see cref="Changed"/> only
/// after one the classifier called substantive, so a consumer can tell a file that was looked at and
/// dismissed from a file that was never looked at.</para>
/// <para>A store that can read the file and still see nothing in it — a section the document spells
/// in another case is the measured one — leaves both states equal for ever, so no examination is
/// ever a change. That is honest and it is also useless on its own, so
/// <see cref="SettingsWatcherOptions{T}.Obstruction"/> lets the store say what is in the way: it is
/// carried on every result and announced once, through <see cref="Failed"/>, when it appears.</para>
/// </remarks>
/// <typeparam name="T">The settings shape.</typeparam>
public sealed class SettingsWatcher<T> : IDisposable where T : class, new()
{
    private const NotifyFilters Notified =
        NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;

    private readonly SettingsWatcherOptions<T> _options;
    private readonly TrailingDebounce _debounce;
    private readonly Lock _gate = new();
    private readonly Lock _examining = new();
    private readonly string _directory;
    private readonly string _name;

    private FileSystemWatcher? _watcher;
    private bool _disposed;
    private int _signals;

    // The obstruction last announced, so one that stands across many examinations is said once.
    // Guarded by _examining, which is the only lock an examination takes to reach it.
    private string? _obstruction;

    /// <summary>Watches the file named by <paramref name="options"/> from this moment.</summary>
    public SettingsWatcher(SettingsWatcherOptions<T> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FilePath, nameof(options));
        ArgumentNullException.ThrowIfNull(options.Read, nameof(options));
        ArgumentNullException.ThrowIfNull(options.Reload, nameof(options));
        ArgumentNullException.ThrowIfNull(options.Classifier, nameof(options));

        _options = options;
        FilePath = Path.GetFullPath(options.FilePath);
        _directory = Path.GetDirectoryName(FilePath)
            ?? throw new ArgumentException("The file to watch has no folder to watch it in.", nameof(options));
        _name = Path.GetFileName(FilePath);

        _debounce = new TrailingDebounce(options.Quiet, options.Time, Examine);

        Directory.CreateDirectory(_directory);
        _watcher = Arm();
    }

    /// <summary>Wires a watcher to a whole-file store in one call.</summary>
    public static SettingsWatcher<T> For(
        SettingsFile<T> file,
        SettingsChangeClassifier<T> classifier,
        TimeSpan? quiet = null,
        TimeProvider? time = null,
        SynchronizationContext? notificationContext = null)
    {
        ArgumentNullException.ThrowIfNull(file);

        var options = new SettingsWatcherOptions<T>(file.FilePath, file.Read, () => file.Reload(), classifier)
        {
            NotificationContext = notificationContext,
        };

        if (quiet is { } window) options = options with { Quiet = window };
        if (time is { } clock) options = options with { Time = clock };

        return new SettingsWatcher<T>(options);
    }

    /// <summary>The file being watched.</summary>
    public string FilePath { get; }

    /// <summary>What the classifier's answer means to the application asking.</summary>
    public string Question => _options.Classifier.Question;

    /// <summary>Raised after every examination, whatever it found. A consumer wanting only the
    /// changes that matter takes <see cref="Changed"/>; this one also says when the file moved and
    /// nothing worth acting on moved with it.</summary>
    public event EventHandler<SettingsChangeEventArgs<T>>? Examined;

    /// <summary>Raised after an examination the classifier called substantive.</summary>
    public event EventHandler<SettingsChangeEventArgs<T>>? Changed;

    /// <summary>Raised when something the watcher does on its own thread does not work, and when the
    /// store first names something standing between it and the file — a
    /// <see cref="SettingsWatchObstructedException"/>, which is the one member of this event that is
    /// not a failure.</summary>
    public event EventHandler<SettingsWatchFailedEventArgs>? Failed;

    /// <summary>Every notification the operating system delivered for this file, before the quiet
    /// window collapses them. Internal because it is the mechanism, not the surface: the tests read
    /// it to prove that a burst really was a burst before asserting that it arrived once.</summary>
    internal int Signals
    {
        get { lock (_gate) return _signals; }
    }

    /// <summary>Raised on each notification, before the quiet window. A test waits on it so it knows
    /// the operating system has delivered before it moves the clock.</summary>
    internal event Action? Signalled;

    public void Dispose()
    {
        FileSystemWatcher? watcher;

        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            watcher = _watcher;
            _watcher = null;
        }

        watcher?.Dispose();
        _debounce.Dispose();
    }

    private FileSystemWatcher Arm()
    {
        // Filtered to the one name, which the rename the atomic write ends in still matches
        // (measured), so the temporary sibling's own notifications never wake anything.
        var watcher = new FileSystemWatcher(_directory, _name)
        {
            IncludeSubdirectories = false,
            NotifyFilter = Notified,
        };

        watcher.Created += OnEntry;
        watcher.Changed += OnEntry;
        watcher.Deleted += OnEntry;

        // The atomic write reaches the file as a rename, never as a change, so a watcher taking only
        // the change notification would sleep through every write this library makes.
        watcher.Renamed += OnEntry;
        watcher.Error += OnError;

        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnEntry(object sender, FileSystemEventArgs e) => Signal();

    private void Signal()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _signals++;
        }

        Guarded(() => Signalled?.Invoke());
        _debounce.Signal();
    }

    // The operating system dropped notifications faster than they could be taken, so what was missed
    // is unknown. The buffer is rebuilt and an examination forced: a missed edit must cost a moment,
    // never a reload that never happens.
    private void OnError(object sender, ErrorEventArgs e)
    {
        Report(e.GetException());

        lock (_gate)
        {
            if (_disposed) return;

            _watcher?.Dispose();
            _watcher = null;

            try
            {
                _watcher = Arm();
            }
            catch (Exception ex) when (AtomicFile.IsFileFailure(ex) || ex is ArgumentException)
            {
                // The folder went away underneath it. Nothing is watched from here, and the failure
                // has been reported; a consumer that cares builds a new watcher.
                Report(ex);
                return;
            }
        }

        Signal();
    }

    private void Examine()
    {
        SettingsChangeEventArgs<T> result;
        string? announce;

        try
        {
            // One examination at a time, so two windows elapsing close together cannot interleave
            // their reads and pair one store's "before" with another's "after".
            lock (_examining)
            {
                lock (_gate)
                {
                    if (_disposed) return;
                }

                var before = _options.Read();
                _options.Reload();
                var after = _options.Read();

                // Asked after the re-read, so it describes the file as it now stands.
                var obstruction = _options.Obstruction?.Invoke();

                result = new SettingsChangeEventArgs<T>(
                    _options.Classifier.Question,
                    before,
                    after,
                    _options.Classifier.IsSubstantive(before, after),
                    obstruction);

                // An obstruction stands until it is repaired, so every examination while it stands
                // would say the same thing. Announced when it appears and when it changes, never
                // again in between; clearing it is not announced, because the next real edit is.
                announce = string.Equals(obstruction, _obstruction, StringComparison.Ordinal)
                    ? null
                    : obstruction;
                _obstruction = obstruction;
            }
        }
        catch (Exception ex)
        {
            // Everything, not only the file failures: this runs on a timer thread, where a throw
            // bypasses the application's unhandled-exception handler and takes the process with it.
            Report(ex);
            return;
        }

        // Before the examination is announced, because it is the reason that examination found
        // nothing and reading them the other way round makes it look like an afterthought. Report
        // posts to the context itself, so this is not wrapped in Notify.
        if (announce is not null) Report(new SettingsWatchObstructedException(FilePath, announce));

        Notify(() => Examined?.Invoke(this, result));
        if (result.IsSubstantive) Notify(() => Changed?.Invoke(this, result));
    }

    private void Notify(Action raise)
    {
        var context = _options.NotificationContext;

        if (context is null) Guarded(raise);
        else context.Post(static state => ((Action)state!)(), raise);
    }

    private void Guarded(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    // Failed goes to the context like everything else. A consumer cannot marshal what it cannot
    // predict, and one event arriving on the interface thread when it carries an obstruction and on
    // a timer thread when it carries a dropped notification is not something correct code can be
    // written against.
    private void Report(Exception error)
    {
        var context = _options.NotificationContext;

        if (context is null) Raise(error);
        else context.Post(static state => ((Action)state!)(), () => Raise(error));
    }

    // Not through Guarded: a Failed subscriber that throws would otherwise be reported through
    // Failed again, and the report is the last place a failure has to go.
    private void Raise(Exception error)
    {
        try
        {
            Failed?.Invoke(this, new SettingsWatchFailedEventArgs(FilePath, error));
        }
        catch (Exception)
        {
            // The last place a failure can be reported has itself failed. Reporting that would
            // recurse, and throwing here would kill the process the guard exists to protect.
        }
    }
}
