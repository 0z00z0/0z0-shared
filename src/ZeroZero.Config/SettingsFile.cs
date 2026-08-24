using System.Globalization;
using System.Text.Json;

namespace ZeroZero.Config;

/// <summary>
/// One JSON file holding one <typeparamref name="T"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Read"/> hands back a snapshot: a fresh instance each call, disconnected from
/// the stored state, so mutating it changes nothing. <see cref="Update"/> is the only route in, and
/// it reads, mutates and saves under one lock acquisition, so a concurrent caller cannot drop a
/// write.</para>
/// <para>A save serialises to a temporary sibling and moves it over the file, so a failed or
/// interrupted write cannot truncate what is already there; a replace the operating system refuses
/// for a moment is retried briefly. It never throws: the returned <see cref="SettingsSaveResult"/>
/// and the <see cref="SaveFailed"/> event carry the failure, and the in-memory state rolls back so
/// it can never disagree with the file.</para>
/// <para>A file that is present but cannot be parsed is copied aside before defaults take over.
/// Missing, empty and unreadable files all end at defaults.</para>
/// </remarks>
/// <typeparam name="T">The settings shape: a class with a parameterless constructor whose
/// defaults are the state a missing file stands for.</typeparam>
public sealed class SettingsFile<T> where T : class, new()
{
    private const string TempSuffix = ".tmp";
    private const string QuarantineMarker = ".bad";
    private const string StampFormat = "yyyy-MM-dd-HHmmss";
    private const int ReplaceAttempts = 5;

    private static readonly TimeSpan ReplacePause = TimeSpan.FromMilliseconds(20);

    private readonly Lock _gate = new();
    private readonly SettingsFileOptions _options;
    private readonly JsonSerializerOptions _serialiser;
    private readonly string _tempPath;

    // The stored state, kept serialised: every read deserialises from it, so no caller ever holds
    // the instance the file is written from.
    private string _json;
    private string? _quarantinePath;

    public SettingsFile(SettingsFileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FileName, nameof(options));

        if (Path.GetFileName(options.FileName) != options.FileName)
        {
            throw new ArgumentException(
                "The file name must carry no directory separator: the host owns the directory, the module owns the name.",
                nameof(options));
        }

        _options = options;
        _serialiser = options.Serialiser;
        FilePath = Path.Combine(options.Directory, options.FileName);
        _tempPath = FilePath + TempSuffix;
        _json = Load();
    }

    /// <summary>The file this instance owns.</summary>
    public string FilePath { get; }

    /// <summary>Where the last unreadable file was preserved, or null if none has been.</summary>
    public string? LastQuarantinePath
    {
        get { lock (_gate) return _quarantinePath; }
    }

    /// <summary>Raised after the stored state changes, always outside the lock because a
    /// subscriber does real work. It arrives on the thread that made the change unless
    /// <see cref="SettingsFileOptions.NotificationContext"/> says otherwise.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a save does not reach the file, outside the lock and on the same thread
    /// as <see cref="Changed"/>.</summary>
    public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed;

    /// <summary>A snapshot of the stored state. Mutating it affects nothing.</summary>
    public T Read()
    {
        lock (_gate) return Deserialise(_json);
    }

    /// <summary>Applies <paramref name="mutate"/> to a draft and saves it. A mutation that throws
    /// leaves the stored state untouched; a save that fails rolls it back.</summary>
    public SettingsSaveResult Update(Action<T> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        Exception? error;
        bool changed;

        lock (_gate)
        {
            var draft = Deserialise(_json);
            mutate(draft);

            var json = Serialise(draft);
            if (string.Equals(json, _json, StringComparison.Ordinal)) return SettingsSaveResult.Success;

            error = TryWrite(json);
            changed = error is null;
            if (changed) _json = json;
        }

        return Report(error, changed);
    }

    /// <summary>Writes the stored state out whether or not it has changed, which is how a file that
    /// was missing or unreadable at load gets its defaults on disk.</summary>
    public SettingsSaveResult Save()
    {
        Exception? error;

        lock (_gate) error = TryWrite(_json);

        return Report(error, changed: false);
    }

    /// <summary>Re-reads the file, quarantining it if it has become unreadable. Returns true, and
    /// raises <see cref="Changed"/>, only when the state on disk differs from the state held.</summary>
    public bool Reload()
    {
        bool changed;

        lock (_gate)
        {
            var json = Load();
            changed = !string.Equals(json, _json, StringComparison.Ordinal);
            if (changed) _json = json;
        }

        if (changed) Notify(() => Changed?.Invoke(this, EventArgs.Empty));
        return changed;
    }

    private SettingsSaveResult Report(Exception? error, bool changed)
    {
        if (changed) Notify(() => Changed?.Invoke(this, EventArgs.Empty));

        if (error is null) return SettingsSaveResult.Success;

        Notify(() => SaveFailed?.Invoke(this, new SettingsSaveFailedEventArgs(FilePath, error)));
        return SettingsSaveResult.Failed(error);
    }

    private void Notify(Action raise)
    {
        var context = _options.NotificationContext;
        if (context is null) raise();
        else context.Post(static state => ((Action)state!)(), raise);
    }

    private T Deserialise(string json) => JsonSerializer.Deserialize<T>(json, _serialiser) ?? new T();

    private string Serialise(T value) => JsonSerializer.Serialize(value, _serialiser);

    // Reads the file into its canonical serialised form, so a comparison later is about values
    // rather than the whitespace a hand edit left behind.
    private string Load()
    {
        string? text;
        try
        {
            text = File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable for a reason the content cannot explain. Nothing is preserved and nothing
            // is overwritten, because the file may still be intact.
            return Serialise(new T());
        }

        // An empty file states nothing, so there is nothing worth preserving.
        if (string.IsNullOrWhiteSpace(text)) return Serialise(new T());

        if (TryCanonicalise(text, out var canonical)) return canonical;

        _quarantinePath = Quarantine() ?? _quarantinePath;

        var defaults = Serialise(new T());
        TryWrite(defaults);
        return defaults;
    }

    private bool TryCanonicalise(string text, out string canonical)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(text, _serialiser);
            if (value is not null)
            {
                canonical = Serialise(value);
                return true;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Malformed, or shaped in a way T cannot accept.
        }

        canonical = string.Empty;
        return false;
    }

    private Exception? TryWrite(string json)
    {
        try
        {
            Directory.CreateDirectory(_options.Directory);
            File.WriteAllText(_tempPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDeleteTemp();
            return ex;
        }

        return Replace();
    }

    // Windows denies a replace for a moment while a scanner, an indexer or a closing handle still
    // holds the file, so a burst of saves meets a refusal that clears on its own. A file that is
    // genuinely locked or read-only still fails, a few milliseconds later.
    private Exception? Replace()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(_tempPath, FilePath, overwrite: true);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (attempt >= ReplaceAttempts)
                {
                    TryDeleteTemp();
                    return ex;
                }

                Thread.Sleep(ReplacePause);
            }
        }
    }

    private void TryDeleteTemp()
    {
        try
        {
            File.Delete(_tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file is replaced by the next write.
        }
    }

    private string? Quarantine()
    {
        var policy = _options.Quarantine;
        if (policy.Keep <= 0) return null;

        var folder = policy.Directory ?? _options.Directory;
        var stem = Path.GetFileNameWithoutExtension(_options.FileName);
        var extension = Path.GetExtension(_options.FileName);

        try
        {
            Directory.CreateDirectory(folder);

            var stamp = DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture);
            var copy = Path.Combine(folder, $"{stem}.{stamp}{QuarantineMarker}{extension}");
            for (var attempt = 2; File.Exists(copy); attempt++)
            {
                copy = Path.Combine(folder, $"{stem}.{stamp}-{attempt}{QuarantineMarker}{extension}");
            }

            File.Copy(FilePath, copy);
            Prune(folder, stem, extension, policy.Keep);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preserving the file is best effort; defaults still take over.
            return null;
        }
    }

    private static void Prune(string folder, string stem, string extension, int keep)
    {
        // The stamp sorts chronologically, so the newest copies are the last by ordinal name.
        var copies = Directory.EnumerateFiles(folder, $"{stem}.*{QuarantineMarker}{extension}")
            .OrderDescending(StringComparer.Ordinal)
            .Skip(keep);

        foreach (var stale in copies)
        {
            try
            {
                File.Delete(stale);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One copy too many costs nothing; failing the load would cost the settings.
            }
        }
    }
}
