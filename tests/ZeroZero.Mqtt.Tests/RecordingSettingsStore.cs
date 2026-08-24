namespace ZeroZero.Mqtt.Tests;

/// <summary>The storage seam over an in-memory document, standing in for a host whose configuration
/// is not a file of the module's own. Counts its writes, because "how many times was the store
/// written" is the question behind several of these tests.</summary>
public sealed class RecordingSettingsStore : IMqttSettingsStore
{
    private MqttSettings _settings = new();

    public int Writes { get; private set; }

    public MqttSettings Read() => _settings.Copy();

    public void Update(Action<MqttSettings> mutate)
    {
        // Read-modify-write against the live document, as the interface requires: a caller holding a
        // snapshot must not roll back whatever a sibling changed meanwhile.
        var draft = _settings.Copy();
        mutate(draft);
        _settings = draft;
        Writes++;
        Changed?.Invoke();
    }

    public event Action? Changed;
}
