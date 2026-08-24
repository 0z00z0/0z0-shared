namespace ZeroZero.Mqtt;

/// <summary>One application-declared group of published things: the key entities carry, the copy a
/// settings panel renders, and where a fresh installation starts.</summary>
/// <param name="DefaultOn">Whether the group is announced before anyone has touched it. A feature
/// group is normally on — the published surface is the point of the feature, and a group is switched
/// off to reduce it, never to opt into it — while a group that costs something to produce is what an
/// operator opts into.</param>
public sealed record PublishGroup(
    string Key, string Label, string Description = "", bool DefaultOn = true);

/// <summary>The group state as it stood at one instant. Taken once and asked many times, so a
/// publish pass cannot announce one entity under the old answer and the next under the new.</summary>
public sealed class PublishGroupSnapshot
{
    private readonly IReadOnlyDictionary<string, bool> _stored;
    private readonly IReadOnlyDictionary<string, PublishGroup> _declared;

    internal PublishGroupSnapshot(
        IReadOnlyDictionary<string, bool> stored, IReadOnlyDictionary<string, PublishGroup> declared)
    {
        _stored = stored;
        _declared = declared;
    }

    /// <summary>Whether a group is announced. A null or empty key means "always published"; a key
    /// nothing declared is on, because an entity carrying an unknown group key must not vanish
    /// silently.</summary>
    public bool IsEnabled(string? key)
    {
        if (string.IsNullOrEmpty(key)) return true;
        if (_stored.TryGetValue(key, out bool on)) return on;
        return !_declared.TryGetValue(key, out var group) || group.DefaultOn;
    }
}

/// <summary>The declared groups and their current state over <see cref="IMqttSettingsStore"/>.</summary>
public sealed class PublishGroupSet
{
    private readonly IMqttSettingsStore _store;
    private readonly Dictionary<string, PublishGroup> _declared;

    public PublishGroupSet(IMqttSettingsStore store, IEnumerable<PublishGroup> groups)
    {
        _store = store;
        _declared = groups.ToDictionary(g => g.Key, StringComparer.Ordinal);
        Declared = [.. _declared.Values];
    }

    /// <summary>The groups the application declared, in declaration order. What a settings panel
    /// renders one row per, and the only vocabulary the module has for a group.</summary>
    public IReadOnlyList<PublishGroup> Declared { get; }

    /// <summary>The state as it stands, for a caller that will ask about several groups. One read of
    /// the store, so every answer in the pass comes from the same moment.</summary>
    public PublishGroupSnapshot Snapshot() =>
        new(new Dictionary<string, bool>(_store.Read().Groups, StringComparer.Ordinal), _declared);

    /// <summary>Whether one group is announced. For a single question; a publish pass takes a
    /// <see cref="Snapshot"/> instead.</summary>
    public bool IsEnabled(string? key) => Snapshot().IsEnabled(key);

    /// <summary>Switches a group. Immediate — a group toggle is one of the two controls that commit
    /// on the spot rather than behind an Apply.</summary>
    public void Set(string key, bool on) => Set([new(key, on)]);

    /// <summary>Switches several groups as one write, so a bulk change raises one notification and
    /// costs one republish rather than one per group.</summary>
    public void Set(IEnumerable<KeyValuePair<string, bool>> states)
    {
        var wanted = states.ToList();
        if (wanted.Count == 0) return;

        _store.Update(s =>
        {
            foreach (var (key, on) in wanted) s.Groups[key] = on;
        });
        Changed?.Invoke();
    }

    /// <summary>Raised after a toggle. Distinct from the settings store's own change event: a group
    /// moving means republish, never reconnect.</summary>
    public event Action? Changed;
}
