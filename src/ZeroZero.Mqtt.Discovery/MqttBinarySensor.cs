namespace ZeroZero.Mqtt.Discovery;

/// <summary>A read-only boolean.</summary>
public sealed class MqttBinarySensor : MqttEntity
{
    public override string Platform => "binary_sensor";

    /// <summary>A receiver ignores an empty payload here and goes on showing the last state it saw, so
    /// an absent reading is published as <see cref="MqttPayload.None"/> — the literal it reads as
    /// unknown.</summary>
    public override string? NoValuePayload => MqttPayload.None;

    /// <summary>The current reading, or null when there is none.</summary>
    public required Func<bool?> Read { get; init; }

    public string PayloadOn { get; init; } = MqttPayload.On;

    public string PayloadOff { get; init; } = MqttPayload.Off;

    /// <inheritdoc cref="MqttSensor.ForceUpdate"/>
    public bool ForceUpdate { get; init; }

    /// <inheritdoc cref="MqttSensor.ExpireAfter"/>
    public int? ExpireAfter { get; init; }

    private protected override string? ReadPayload() =>
        Read() switch { true => PayloadOn, false => PayloadOff, null => null };

    internal override void Describe(DiscoveryKeys keys)
    {
        keys.Set("payload_on", PayloadOn);
        keys.Set("payload_off", PayloadOff);
        keys.SetWhenTrue("force_update", ForceUpdate);
        keys.Set("expire_after", ExpireAfter);
    }
}
