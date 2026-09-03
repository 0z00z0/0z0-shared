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
/// too.</para>
/// </remarks>
public sealed class SectionedSettingsFile
{
    private const string QuarantineMarker = ".bad";
    private const string StampFormat = "yyyy-MM-dd-HHmmss";

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

            changed = [.. _sections.Where(section => Differs(_document, loaded.Document, section.Name))];
            _document = loaded.Document;
            _hasLoaded = true;
            _isFromNewerVersion = loaded.IsFromNewerVersion;
        }

        foreach (var section in changed) Notify(section.RaiseChanged);
        return changed.Count > 0;
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

    internal SettingsSaveResult WriteSection<T>(string name, Func<T, T> produce, ISectionNotification section)
        where T : class, new()
    {
        Exception? error;
        bool changed;

        lock (_gate) error = Apply(name, produce, out changed);

        if (changed) Notify(section.RaiseChanged);
        if (error is null) return SettingsSaveResult.Success;

        Notify(() => SaveFailed?.Invoke(this, new SettingsSaveFailedEventArgs(FilePath, error)));
        return SettingsSaveResult.Failed(error);
    }

    private Exception? Apply<T>(string name, Func<T, T> produce, out bool changed) where T : class, new()
    {
        changed = false;

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

        var document = Rebase(content, outcome);

        if (document.Version > _options.Version)
        {
            _document = document;
            _isFromNewerVersion = true;
            return new InvalidOperationException(
                $"The document declares version {document.Version?.ToString(CultureInfo.InvariantCulture)}, above the {_options.Version.ToString(CultureInfo.InvariantCulture)} this build writes, so it is neither read nor written.");
        }

        _isFromNewerVersion = false;

        if (document.TryReadSection<T>(name, _options.Serialiser, out var draft) == SectionOutcome.Unparseable)
        {
            Quarantine(document.Content);
        }

        var value = produce(draft);
        var written = document.WriteSection(name, value, _options.Serialiser, _options.SectionOrder, _options.Version);

        if (written is null)
        {
            // Nothing of this section changed, but the document on disk may have moved on.
            _document = document;
            return null;
        }

        if (AtomicFile.Write(FilePath, written) is { } failure) return failure;

        _document = SettingsDocument.TryParse(written)
            ?? throw new InvalidOperationException("The document this store just wrote does not parse as one.");
        changed = true;
        return null;
    }

    // What a write builds on: the document on disk when it is usable, a fresh one when it is not.
    private SettingsDocument Rebase(byte[]? content, FileOutcome outcome)
    {
        if (outcome is FileOutcome.Missing or FileOutcome.Blank)
        {
            return SettingsDocument.Empty(content is null ? JsonLayout.Default : JsonLayout.Detect(content));
        }

        if (SettingsDocument.TryParse(content!) is { } document) return document;

        // Unusable content, and the latch is set, so this is the self-heal: the copy is taken from the
        // bytes in hand and a whole document is written in place of the broken one.
        Quarantine(content!);
        return SettingsDocument.Empty(JsonLayout.Detect(content!));
    }

    private LoadedDocument? Load()
    {
        var outcome = ReadFile(out var content);
        if (outcome == FileOutcome.Unreadable) return null;

        if (outcome is FileOutcome.Missing or FileOutcome.Blank)
        {
            var empty = SettingsDocument.Empty(content is null ? JsonLayout.Default : JsonLayout.Detect(content));
            return new LoadedDocument(empty, false);
        }

        if (SettingsDocument.TryParse(content!) is not { } parsed)
        {
            Quarantine(content!);
            return new LoadedDocument(SettingsDocument.Empty(JsonLayout.Detect(content!)), false);
        }

        return new LoadedDocument(parsed, parsed.Version > _options.Version);
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

    // The copy is written from the bytes already read. Copying the file instead would fail for
    // exactly the file whose second read fails, which is the case a copy is most needed for.
    private void Quarantine(byte[] content)
    {
        var policy = _options.Quarantine;
        if (policy.Keep <= 0) return;
        if (_quarantined is not null && _quarantined.AsSpan().SequenceEqual(content)) return;

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

            if (AtomicFile.Write(copy, content) is not null) return;

            _quarantinePath = copy;
            _quarantined = content;
            Prune(folder, stem, extension, policy.Keep);
        }
        catch (Exception ex) when (AtomicFile.IsFileFailure(ex))
        {
            // Preserving the document is best effort; nothing about the read depends on it.
        }
    }

    private static void Prune(string folder, string stem, string extension, int keep)
    {
        // The stamp sorts chronologically, so the newest copies are the last by ordinal name.
        var copies = Directory.EnumerateFiles(folder, $"{stem}.*{QuarantineMarker}{extension}")
            .OrderDescending(StringComparer.Ordinal)
            .Skip(keep);

        foreach (var stale in copies) AtomicFile.TryDelete(stale);
    }

    private void Notify(Action raise)
    {
        var context = _options.NotificationContext;
        if (context is null) raise();
        else context.Post(static state => ((Action)state!)(), raise);
    }

    private readonly record struct LoadedDocument(SettingsDocument Document, bool IsFromNewerVersion);

    private enum FileOutcome
    {
        Missing,
        Blank,
        Present,
        Unreadable,
    }
}
