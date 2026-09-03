namespace ZeroZero.Config.Sections;

/// <summary>What a section store hands the document so a change can be announced.</summary>
internal interface ISectionNotification
{
    string Name { get; }

    void RaiseChanged();
}

/// <summary>A store over one named section of a sectioned document.</summary>
/// <remarks>
/// <para>Three members carry the whole of it: a snapshot read, a mutation, and a change event. What
/// sits behind them — a section of a document shared with other components, or a file of its own —
/// is not the component's business, which is what lets a component declare its storage dependency
/// without declaring where its settings live.</para>
/// <para><see cref="Update"/> reads, mutates and writes under one lock acquisition against the
/// document that is on disk at that moment, so a caller holding an old snapshot commits its own
/// field without rolling back what a sibling changed meanwhile.</para>
/// </remarks>
/// <typeparam name="T">The section's shape.</typeparam>
public sealed class SettingsSection<T> : ISectionNotification where T : class, new()
{
    private readonly SectionedSettingsFile _document;

    internal SettingsSection(SectionedSettingsFile document, string name)
    {
        _document = document;
        Name = name;
    }

    /// <summary>The document key this store owns.</summary>
    public string Name { get; }

    /// <summary>Whether the document carries this section at all. False for a document written before
    /// the section existed, and for one this build has refused as too new.</summary>
    public bool IsPresent => _document.ReadSection<T>(Name, out _) != SectionOutcome.Missing;

    /// <summary>Whether the section is present but this build cannot read it — a value of the wrong
    /// kind, or an enum member no type answers to. The section reads as its defaults; its bytes stay
    /// in the file, and the document has been copied aside.</summary>
    public bool IsUnreadable => _document.ReadSection<T>(Name, out _) == SectionOutcome.Unparseable;

    /// <summary>Raised after this section's stored state changes, outside the document's lock because
    /// a subscriber does real work.</summary>
    public event EventHandler? Changed;

    /// <summary>A snapshot of this section. Mutating it affects nothing; a section the document does
    /// not carry, or one this build cannot read, reads as the type's own defaults.</summary>
    public T Read()
    {
        _document.ReadSection<T>(Name, out var value);
        return value;
    }

    /// <summary>Applies <paramref name="mutate"/> to a draft taken from the document on disk and
    /// writes it back. A mutation that throws leaves the file untouched.</summary>
    public SettingsSaveResult Update(Action<T> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        return _document.WriteSection<T>(Name, draft => { mutate(draft); return draft; }, this);
    }

    /// <summary>Writes <paramref name="value"/> as this section, whatever the document currently
    /// says. This is how a section the document does not yet carry gets its defaults on disk.</summary>
    public SettingsSaveResult Write(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return _document.WriteSection<T>(Name, _ => value, this);
    }

    void ISectionNotification.RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
