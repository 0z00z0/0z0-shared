namespace ZeroZero.Mqtt;

/// <summary>What one read of a channel's payload produced. The three cases are distinct because they
/// call for different things on the wire.</summary>
public enum MqttPayloadStatus
{
    /// <summary>A value, ready to publish.</summary>
    Value,

    /// <summary>No current reading. The topic is emptied, so a consumer connecting later sees
    /// nothing rather than a value of unknown age.</summary>
    None,

    /// <summary>The payload function threw. Nothing is known about the current value, so the last
    /// one published stands and the next pass tries again.</summary>
    Failed,
}

/// <summary>One retained topic, the function producing its payload, and its retain, QoS and dedupe
/// slot. The application declares one per value it publishes — one bare topic per entity, carrying a
/// plain value.</summary>
/// <param name="Key">The topic segment below <c>&lt;topicRoot&gt;/&lt;deviceId&gt;/</c>, and the
/// dedupe slot's name. Normally the entity id. May not be
/// <see cref="MqttTopics.AvailabilityKey"/> and may carry no topic separator or wildcard.</param>
/// <param name="Payload">The current payload, or null when there is none to publish. Called on a
/// background thread, never on the caller that signalled.</param>
/// <param name="Debounce">How long a requested pass waits before reading <paramref name="Payload"/>.
/// Non-zero lets an in-progress write land before the read that reports it, and collapses a burst of
/// signals into one read.</param>
/// <param name="RepublishLastOnConnect">On connect, when <paramref name="Payload"/> yields nothing,
/// whether the last payload published is sent again. True for a channel whose producer has a first
/// reading to wait for; false for one that is always readable.</param>
public sealed record MqttChannel(
    string Key,
    Func<string?> Payload,
    bool Retain = true,
    MqttQos Qos = MqttQos.AtLeastOnce,
    TimeSpan Debounce = default,
    bool RepublishLastOnConnect = false);

/// <summary>The declared channels, the dedupe cache across all of them, and one coalescing gate per
/// channel. Guarded by a single lock, because the producer threads, the command worker and the
/// maintain loop all touch it.</summary>
public sealed class MqttChannelSet
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, MqttChannel> _channels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastPayload = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CoalescingGate> _gates = new(StringComparer.Ordinal);

    public MqttChannelSet(IEnumerable<MqttChannel> channels) => Replace(channels);

    public IReadOnlyList<MqttChannel> Channels
    {
        get { lock (_lock) return [.. _channels.Values]; }
    }

    public IReadOnlyList<string> Keys
    {
        get { lock (_lock) return [.. _channels.Keys]; }
    }

    public MqttChannel? Find(string key)
    {
        lock (_lock) return _channels.GetValueOrDefault(key);
    }

    /// <summary>Swaps the declared set, returning the keys that have gone so the caller can empty
    /// their retained topics. The dedupe entries of channels that survive are kept, so a rebuild
    /// does not re-send every unchanged payload; the entries of channels that have gone are dropped
    /// along with their gates.</summary>
    /// <exception cref="ArgumentException">A key is unusable, or two channels share one. Both are
    /// declaration mistakes that would otherwise publish silently to the wrong topic.</exception>
    public IReadOnlyList<string> Replace(IEnumerable<MqttChannel> channels)
    {
        var next = new Dictionary<string, MqttChannel>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            if (MqttTopics.ValidateChannelKey(channel.Key) is { } error)
                throw new ArgumentException(error, nameof(channels));
            if (!next.TryAdd(channel.Key, channel))
                throw new ArgumentException(
                    $"Two channels share the key '{channel.Key}'.", nameof(channels));
        }

        lock (_lock)
        {
            var departed = _channels.Keys.Where(k => !next.ContainsKey(k)).ToList();

            _channels.Clear();
            foreach (var (key, channel) in next)
            {
                _channels[key] = channel;
                _gates.TryAdd(key, new CoalescingGate());
            }
            foreach (string gone in departed)
            {
                _lastPayload.Remove(gone);
                _gates.Remove(gone);
            }
            return departed;
        }
    }

    /// <summary>Records a signal for one channel, returning true only to the caller that must start
    /// the publishing loop for it.</summary>
    public bool Signal(string key) => Gate(key)?.Signal() ?? false;

    public void BeginPass(string key) => Gate(key)?.BeginPass();

    public bool ShouldRepeat(string key) => Gate(key)?.ShouldRepeat() ?? false;

    private CoalescingGate? Gate(string key)
    {
        lock (_lock) return _gates.GetValueOrDefault(key);
    }

    /// <summary>Compare-and-set: false when the payload matches what the slot already holds, so an
    /// unchanged value is cached but not sent. Done under the lock, so a stale write cannot dedupe
    /// the next real change. The cache is updated while disconnected too, ready for the next
    /// connect.</summary>
    public bool Accept(string key, string payload)
    {
        lock (_lock)
        {
            if (_lastPayload.TryGetValue(key, out string? previous)
                && string.Equals(payload, previous, StringComparison.Ordinal)) return false;
            _lastPayload[key] = payload;
            return true;
        }
    }

    /// <summary>Takes the dedupe slot without asking whether the payload changed — for the passes
    /// that must publish regardless: a (re)connect, and a "publish now" the user is watching.</summary>
    public void Force(string key, string payload)
    {
        lock (_lock) _lastPayload[key] = payload;
    }

    public string? LastPayload(string key)
    {
        lock (_lock) return _lastPayload.GetValueOrDefault(key);
    }

    /// <summary>Whether anything has been published on a channel yet. What decides whether a channel
    /// with no current reading needs its topic emptied or has nothing standing to contradict.</summary>
    public bool HasPublished(string key)
    {
        lock (_lock) return _lastPayload.ContainsKey(key);
    }

    /// <summary>Empties the whole cache, so the next pass re-sends every channel.</summary>
    public void Forget()
    {
        lock (_lock) _lastPayload.Clear();
    }

    /// <summary>Empties one channel's slot, so the next pass re-sends it. What rolls a dedupe entry
    /// back when the send it was recorded for did not reach the broker.</summary>
    public void Forget(string key)
    {
        lock (_lock) _lastPayload.Remove(key);
    }
}
