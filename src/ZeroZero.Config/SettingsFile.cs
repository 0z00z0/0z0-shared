using System.Text;
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
/// <para>A file that is present but cannot be parsed is copied aside before defaults take over; when
/// the policy keeps copies and the copy fails, the file counts as unreadable and is not written over.
/// Missing, empty and unreadable files all end at defaults — but a file that could not be read at
/// all may still be intact, so nothing is written over it until a read has succeeded. That latch is
/// set once and never cleared: a later read that fails, or finds the file broken, does not stop a
/// good configuration being written back over it.</para>
/// </remarks>
/// <typeparam name="T">The settings shape: a class with a parameterless constructor whose
/// defaults are the state a missing file stands for.</typeparam>
public sealed class SettingsFile<T> where T : class, new()
{
    private readonly Lock _gate = new();
    private readonly SettingsFileOptions _options;
    private readonly JsonSerializerOptions _serialiser;

    // The stored state, kept serialised: every read deserialises from it, so no caller ever holds
    // the instance the file is written from.
    private string _json;
    private string? _quarantinePath;

    // Whether any read has ever succeeded. Until one has, memory holds only defaults and the file
    // may hold the user's settings behind a lock, so a write would put nothing over something.
    private bool _hasLoaded;

    // Whether the file on disk failed to parse and its quarantine copy failed too. Nothing is written
    // over it until a read finds it otherwise.
    private bool _uncopied;

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

        var loaded = Load();
        _hasLoaded = loaded is not null;
        _json = loaded ?? Serialise(new T());
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

            error = TryWriteOnceLoaded(json);
            changed = error is null;
            if (changed) _json = json;
        }

        return Report(error, changed);
    }

    /// <summary>Writes the stored state out whether or not it has changed, which is how a file that
    /// was missing at load gets its defaults on disk. Refused, like <see cref="Update"/>, while the
    /// file has never been read.</summary>
    public SettingsSaveResult Save()
    {
        Exception? error;

        lock (_gate) error = TryWriteOnceLoaded(_json);

        return Report(error, changed: false);
    }

    /// <summary>Re-reads the file, quarantining it if it has become unreadable. Returns true, and
    /// raises <see cref="Changed"/>, only when the state on disk differs from the state held. A file
    /// that cannot be read at all leaves the held state standing.</summary>
    public bool Reload()
    {
        bool changed;

        lock (_gate)
        {
            if (Load() is not { } json) return false;

            _hasLoaded = true;
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
    // rather than the whitespace a hand edit left behind. Null when the file could not be read.
    private string? Load()
    {
        byte[]? content;
        try
        {
            content = File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable for a reason the content cannot explain, so the file may still be intact:
            // nothing is preserved, and the caller decides what stands in.
            return null;
        }

        _uncopied = false;
        var text = content is null ? null : Decode(content);

        // An empty file states nothing, so there is nothing worth preserving.
        if (string.IsNullOrWhiteSpace(text)) return Serialise(new T());

        if (TryCanonicalise(text, out var canonical)) return canonical;

        var copy = QuarantineCopy.TryWrite(_options.Quarantine, _options.Directory, _options.FileName, content!);
        if (copy is null && _options.Quarantine.Keep > 0)
        {
            // The file is the only copy of what it holds, so it is read as unreadable, not replaced.
            _uncopied = true;
            return null;
        }

        _quarantinePath = copy ?? _quarantinePath;

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

    private Exception? TryWriteOnceLoaded(string json)
    {
        if (!_hasLoaded)
        {
            return new InvalidOperationException(
                "The file has not been read since the store opened, so it is not written over: what is on disk may be intact. Reload it first.");
        }

        if (_uncopied)
        {
            return new IOException(
                "The file cannot be parsed and its quarantine copy could not be written, so it is not written over. Reload it once the copy can be written.");
        }

        return TryWrite(json);
    }

    private Exception? TryWrite(string json) => AtomicFile.WriteText(FilePath, json);

    // The decoding File.ReadAllText applies: UTF-8 unless a byte-order mark names another encoding.
    private static string Decode(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
