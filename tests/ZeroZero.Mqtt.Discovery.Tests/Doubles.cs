namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>Somewhere to publish that records what it was asked to send and how. Both questions
/// matter: what reached the wire, and whether a pass that touches every entity did so as one batch
/// rather than as one round trip each.</summary>
public sealed class RecordingPublisher : IMqttPublisher
{
    private readonly List<IReadOnlyList<MqttMessage>> _calls = [];

    /// <summary>Which topics the broker refuses. What a half-landed pass is made of.</summary>
    public Func<string, bool> Refuses { get; set; } = _ => false;

    /// <summary>One entry per call, whether it carried one message or forty.</summary>
    public IReadOnlyList<IReadOnlyList<MqttMessage>> Calls => _calls;

    /// <summary>Every message, in the order it was sent.</summary>
    public IReadOnlyList<MqttMessage> Messages => [.. _calls.SelectMany(c => c)];

    /// <summary>Calls that carried at least one message — an empty batch costs nothing and is not a
    /// round trip.</summary>
    public int RoundTrips => _calls.Count(c => c.Count > 0);

    public string? Last(string topic) =>
        Messages.Where(m => m.Topic == topic).Select(m => m.Payload).LastOrDefault();

    public int CountOn(string topic) => Messages.Count(m => m.Topic == topic);

    public bool Emptied(string topic) => Last(topic) == "";

    public IReadOnlyList<string> Topics => [.. Messages.Select(m => m.Topic)];

    public void Forget() => _calls.Clear();

    public Task<bool> PublishAsync(
        string topic, string payload, bool retain,
        MqttQos qos = MqttQos.AtLeastOnce, CancellationToken ct = default)
    {
        _calls.Add([new MqttMessage(topic, payload, retain, qos)]);
        return Task.FromResult(!Refuses(topic));
    }

    public Task<bool> PublishAsync(IEnumerable<MqttMessage> messages, CancellationToken ct = default)
    {
        var batch = messages.ToList();
        _calls.Add(batch);
        return Task.FromResult(batch.TrueForAll(m => !Refuses(m.Topic)));
    }
}

/// <summary>The broker settings over an in-memory document, so a <see cref="PublishGroupSet"/> can be
/// built without a file.</summary>
public sealed class MemorySettingsStore : IMqttSettingsStore
{
    private MqttSettings _settings = new();

    public MqttSettings Read() => _settings.Copy();

    public void Update(Action<MqttSettings> mutate)
    {
        var draft = _settings.Copy();
        mutate(draft);
        _settings = draft;
        Changed?.Invoke();
    }

    public event Action? Changed;
}

/// <summary>The ledger over an in-memory document, counting its writes: "how often was the record
/// rewritten" is the question behind the half-landed pass.</summary>
public sealed class RecordingLedgerStore : IDiscoveryLedgerStore
{
    private DiscoveryLedger _ledger = new();

    public int Writes { get; private set; }

    public DiscoveryLedger Read() => _ledger.Copy();

    public void Update(Action<DiscoveryLedger> mutate)
    {
        var draft = _ledger.Copy();
        mutate(draft);
        _ledger = draft;
        Writes++;
    }
}
