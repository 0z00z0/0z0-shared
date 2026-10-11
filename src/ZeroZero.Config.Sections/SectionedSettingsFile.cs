using System.Globalization;

namespace ZeroZero.Config.Sections;

/// <summary>One JSON document whose top-level keys are sections, each owned by whatever component
/// asked for it.</summary>
/// <remarks>
/// <para>A section is addressed, never the document. Reading binds one section's bytes to one type;
/// writing replaces the byte ranges of the values that changed. Every other byte — a sibling
/// section, a section this build has no type for, a value from a version that no longer exists, a
/// hand-written comment, the file's own key order — is copied across untouched, because nothing ever
/// rebuilds the document from a type.</para>
/// <para>Writing is refused until a read has succeeded, and that latch is set once and never
/// cleared: a file held open by another process may be perfectly intact, so nothing is written over
/// it; but once anything has been read, writing a good configuration over a file broken by hand is
/// the intended repair and stays allowed.</para>
/// <para>A document this build cannot read is copied aside from the bytes already in hand, never by
/// reading the file a second time — the copy has to work for the file whose second read would fail
/// too. When the policy keeps copies and the copy fails, the document is not written over.</para>
/// <para>A refused write is returned, never raised: <see cref="SettingsSaveResult"/> carries the
/// reason and <see cref="SaveFailed"/> announces it. A caller that reads neither loses every write
/// in silence, and a refusal that stands — a key differing only in case — refuses every later write
/// the same way, so nothing the person changes ever reaches the file.</para>
/// <para>Top-level keys are matched letter for letter. The serialiser's case-insensitive matching
/// binds a section's members and reaches no further, so a section addressed under one spelling while
/// the file carries another reads as its type's defaults, and the store's own
/// <c>ConfigVersion</c> key stands beside a document's <c>Version</c> or <c>version</c> without
/// either being renamed.</para>
/// </remarks>
public sealed class SectionedSettingsFile
{
    private readonly Lock _gate = new();
    private readonly SectionedSettingsOptions _options;
    private readonly List<ISectionNotification> _sections = [];

    private SettingsDocument _document;
    private string? _quarantinePath;
    private byte[]? _quarantined;

    // Whether any read has ever succeeded. Until one has, the file may hold the user's settings
    // behind a lock and memory holds nothing, so a write would put nothing over something.
    private bool _hasLoaded;
    private bool _isFromNewerVersion;

    public SectionedSettingsFile(SectionedSettingsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FileName, nameof(options));

        if (Path.GetFileName(options.FileName) != options.FileName)
        {
            throw new ArgumentException(
                "The file name must carry no directory separator: the host owns the directory, the document owns the name.",
                nameof(options));
        }

        _options = options;
        FilePath = Path.Combine(options.Directory, options.FileName);
        _document = SettingsDocument.Empty(JsonLayout.Default);

        if (Load() is { } loaded)
        {
            _document = loaded.Document;
            _hasLoaded = true;
            _isFromNewerVersion = loaded.IsFromNewerVersion;
        }
    }

    /// <summary>The document this instance owns.</summary>
    public string FilePath { get; }

    /// <summary>The version the document declares, or null when it carries no version key — the older
    /// shape, which is read as it stands.</summary>
    public int? DocumentVersion
    {
        get { lock (_gate) return _document.Version; }
    }

    /// <summary>Whether the document declares a version above the one this build writes. Nothing is
    /// read from it and nothing is written to it: a newer peer owns keys this build would not
    /// understand, and defaults written over them would be the loss the design exists to prevent.</summary>
    public bool IsFromNewerVersion
    {
        get { lock (_gate) return _isFromNewerVersion; }
    }

    /// <summary>Whether any read has succeeded. While false every write is refused.</summary>
    public bool HasLoaded
    {
        get { lock (_gate) return _hasLoaded; }
    }

    /// <summary>Where the last document this build could not read was preserved, or null if none has
    /// been.</summary>
    public string? LastQuarantinePath
    {
        get { lock (_gate) return _quarantinePath; }
    }

    /// <summary>Every top-level key the document carries, in file order, whether or not this build has
    /// a type for it.</summary>
    public IReadOnlyList<string> Keys
    {
        get { lock (_gate) return _document.Keys; }
    }

    /// <summary>Raised when a write does not reach the file.</summary>
    public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed;

    /// <summary>A store over one named section. Several stores may address one document; each sees
    /// only its own section.</summary>
    /// <typeparam name="T">The section's shape: a class with a parameterless constructor that
    /// serialises to a JSON object.</typeparam>
    public SettingsSection<T> Section<T>(string name) where T : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // A section is a JSON object by definition, so a type that serialises to anything else is
        // caught at wire-up rather than on the first save a person makes.
        var probe = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new T(), _options.Serialiser);
        if (JsonObjectSpans.TryReadDocument(probe) is null)
        {
            throw new ArgumentException(
                $"The type behind section '{name}' does not serialise to a JSON object, so it cannot be a section.",
                nameof(name));
        }

        var section = new SettingsSection<T>(this, name);

        lock (_gate)
        {
            _sections.Add(section);

            // Binding it once now is what takes the copy of a section this build cannot read, before
            // anything has had the chance to write over it.
            if (_document.TryReadSection<T>(name, _options.Serialiser, out _) == SectionOutcome.Unparseable)
            {
                Quarantine(_document.Content);
            }
        }

        return section;
    }

    /// <summary>Re-reads the document. Raises <see cref="SettingsSection{T}.Changed"/> on every
    /// section whose bytes differ from the ones held, and returns true when any did. A document that
    /// cannot be read at all leaves the held state standing.</summary>
    public bool Reload()
    {
        List<ISectionNotification> changed;

        lock (_gate)
        {
            if (Load() is not { } loaded) return false;

            changed = Hold(loaded.Document);
            _hasLoaded = true;
            _isFromNewerVersion = loaded.IsFromNewerVersion;
        }

        foreach (var section in changed) Notify(section.RaiseChanged);
        return changed.Count > 0;
    }

    internal string? ConflictingKey(string name)
    {
        lock (_gate) return _document.ConflictingKey(name);
    }

    internal SectionOutcome ReadSection<T>(string name, out T value) where T : class, new()
    {
        lock (_gate)
        {
            if (_isFromNewerVersion)
            {
                value = new T();
                return SectionOutcome.Missing;
            }

            return _document.TryReadSection(name, _options.Serialiser, out value);
        }
    }

    internal SettingsSaveResult WriteSection<T>(string name, Func<T, T> produce) where T : class, new()
    {
        Exception? error;
        List<ISectionNotification> changed;

        lock (_gate) error = Apply(name, produce, out changed);

        foreach (var moved in changed) Notify(moved.RaiseChanged);
        if (error is null) return SettingsSaveResult.Success;

        Notify(() => SaveFailed?.Invoke(this, new SettingsSaveFailedEventArgs(FilePath, error)));
        return SettingsSaveResult.Failed(error);
    }

    // Every path that takes a new document in announces each section it moved, whichever section the
    // write was for: a hand edit to a sibling lands in memory here, and a later reload or watcher
    // comparing against memory would find nothing left to announce.
    private Exception? Apply<T>(string name, Func<T, T> produce, out List<ISectionNotification> changed)
        where T : class, new()
    {
        changed = [];

        if (!_hasLoaded)
        {
            return new InvalidOperationException(
                "The document has not been read since the store opened, so it is not written over: what is on disk may be intact. Reload it first.");
        }

        // The document on disk decides, not what memory remembers: an edit made out of band since the
        // last read is part of the file this write has to preserve.
        var outcome = ReadFile(out var content);
        if (outcome == FileOutcome.Unreadable)
        {
            return new IOException(
                "The document could not be read, so it is not written over: what is on disk may be intact.");
        }

        if (Rebase(content, outcome) is not { } document) return Uncopied();

        if (document.Version > _options.Version)
        {
            changed = Hold(document);
            _isFromNewerVersion = true;
            return new InvalidOperationException(
                $"The document declares version {document.Version?.ToString(CultureInfo.InvariantCulture)}, above the {_options.Version.ToString(CultureInfo.InvariantCulture)} this build writes, so it is neither read nor written.");
        }

        _isFromNewerVersion = false;

        if (document.TryReadSection<T>(name, _options.Serialiser, out var draft) == SectionOutcome.Unparseable
            && !Quarantine(document.Content))
        {
            changed = Hold(document);
            return Uncopied();
        }

        var value = produce(draft);

        byte[]? written;
        try
        {
            written = document.WriteSection(name, value, _options.Serialiser, _options.SectionOrder, _options.Version);
        }
        catch (SettingsKeyCaseConflictException conflict)
        {
            // Nothing is written: a key differing only in case would retire the one already there.
            changed = Hold(document);
            return conflict;
        }

        if (written is null)
        {
            // Nothing of this section changed, but the document on disk may have moved on.
            changed = Hold(document);
            return null;
        }

        if (AtomicFile.Write(FilePath, written) is { } failure) return failure;

        changed = Hold(SettingsDocument.TryParse(written)
            ?? throw new InvalidOperationException("The document this store just wrote does not parse as one."));
        return null;
    }

    // Takes a new document as the one held, and returns every section whose bytes it moved.
    private List<ISectionNotification> Hold(SettingsDocument document)
    {
        List<ISectionNotification> changed = [.. _sections.Where(section => Differs(_document, document, section.Name))];
        _document = document;
        return changed;
    }

    private static IOException Uncopied() => new(
        "The document cannot be read by this build and its quarantine copy could not be written, so it is not written over.");

    // What a write builds on: the document on disk when it is usable, a fresh one when it is not.
    // Null when it is unusable and its copy could not be taken.
    private SettingsDocument? Rebase(byte[]? content, FileOutcome outcome)
    {
        if (outcome is FileOutcome.Missing or FileOutcome.Blank)
        {
            return SettingsDocument.Empty(content is null ? JsonLayout.Default : JsonLayout.Detect(content));
        }

        if (SettingsDocument.TryParse(content!) is { } document) return document;

        // Unusable content: the copy is taken from the bytes in hand, and a write puts a whole document
        // in place of the broken one.
        return Quarantine(content!) ? SettingsDocument.Empty(JsonLayout.Detect(content!)) : null;
    }

    // Opening writes nothing, so a document whose copy failed still reads as empty here.
    private (SettingsDocument Document, bool IsFromNewerVersion)? Load()
    {
        var outcome = ReadFile(out var content);
        if (outcome == FileOutcome.Unreadable) return null;

        var document = Rebase(content, outcome) ?? SettingsDocument.Empty(JsonLayout.Detect(content!));
        return (document, document.Version > _options.Version);
    }

    private FileOutcome ReadFile(out byte[]? content)
    {
        content = null;

        try
        {
            if (!File.Exists(FilePath)) return FileOutcome.Missing;
            content = File.ReadAllBytes(FilePath);
        }
        catch (Exception ex) when (AtomicFile.IsFileFailure(ex))
        {
            // Unreadable for a reason the content cannot explain, so the file may still be intact.
            return FileOutcome.Unreadable;
        }

        return JsonObjectSpans.IsBlank(content) ? FileOutcome.Blank : FileOutcome.Present;
    }

    private static bool Differs(SettingsDocument before, SettingsDocument after, string name) =>
        !before.SectionContent(name).SequenceEqual(after.SectionContent(name));

    // Whether the content may be written over: copied now or already, or the policy keeps no copies.
    private bool Quarantine(byte[] content)
    {
        if (_options.Quarantine.Keep <= 0) return true;
        if (_quarantined is not null && _quarantined.AsSpan().SequenceEqual(content)) return true;

        if (QuarantineCopy.TryWrite(_options.Quarantine, _options.Directory, _options.FileName, content) is not { } copy)
        {
            return false;
        }

        _quarantinePath = copy;
        _quarantined = content;
        return true;
    }

    private void Notify(Action raise)
    {
        var context = _options.NotificationContext;
        if (context is null) raise();
        else context.Post(static state => ((Action)state!)(), raise);
    }

    private enum FileOutcome
    {
        Missing,
        Blank,
        Present,
        Unreadable,
    }
}
