namespace ZeroZero.Mqtt.Discovery;

/// <summary>A read-only boolean.</summary>
public sealed class MqttBinarySensor : MqttEntity
{
    public override string Platform => "binary_sensor";

    /// <summary>An empty payload matches neither declared payload, which is how a receiver is told
    /// the state is unknown.</summary>
    public override string? NoValuePayload => null;

    /// <summary>The current reading, or null when there is none.</summary>
    public required Func<bool?> Read { get; init; }

    public string PayloadOn { get; init; } = MqttPayload.On;

    public string PayloadOff { get; init; } = MqttPayload.Off;

    private protected override string? ReadPayload() =>
        Read() switch { true => PayloadOn, false => PayloadOff, null => null };

    internal override void Describe(DiscoveryKeys keys)
    {
        keys.Set("payload_on", PayloadOn);
        keys.Set("payload_off", PayloadOff);
    }
}
