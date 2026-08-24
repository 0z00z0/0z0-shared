namespace ZeroZero.Mqtt.Discovery;

/// <summary>A boolean the receiver can also write.</summary>
public sealed class MqttSwitch : MqttCommandEntity
{
    public override string Platform => "switch";

    /// <summary>A receiver ignores an empty payload here and goes on showing the last state it saw, so
    /// an absent reading is published as <see cref="MqttPayload.None"/> — the literal it reads as
    /// unknown.</summary>
    public override string? NoValuePayload => MqttPayload.None;

    /// <summary>The current reading, or null when there is none.</summary>
    public required Func<bool?> Read { get; init; }

    /// <summary>What to do with an inbound boolean the payload parsed to. Returns a refusal carrying
    /// a reason, or the work to run.</summary>
    public required Func<bool, MqttCommandVerdict> Apply { get; init; }

    public string PayloadOn { get; init; } = MqttPayload.On;

    public string PayloadOff { get; init; } = MqttPayload.Off;

    public override MqttCommandVerdict Accept(string payload) =>
        MqttPayload.ReadFlag(payload, PayloadOn, PayloadOff) is { } wanted
            ? Apply(wanted)
            : MqttCommandVerdict.Malformed($"Expected '{PayloadOn}' or '{PayloadOff}'.");

    private protected override string? ReadPayload() =>
        Read() switch { true => PayloadOn, false => PayloadOff, null => null };

    internal override void Describe(DiscoveryKeys keys)
    {
        // state_on and state_off default to these, so the pair is declared once.
        keys.Set("payload_on", PayloadOn);
        keys.Set("payload_off", PayloadOff);
    }
}
