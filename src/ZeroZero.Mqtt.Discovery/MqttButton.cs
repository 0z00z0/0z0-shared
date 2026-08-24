namespace ZeroZero.Mqtt.Discovery;

/// <summary>A press. Command-only: it declares no state channel, contributes no channel to the
/// connection, publishes no payload, and owns no state topic to evict.</summary>
/// <remarks>A button has nothing to report between presses, so a state topic would carry an empty
/// retained payload nothing ever reads.</remarks>
public sealed class MqttButton : MqttCommandEntity
{
    /// <summary>The payload a button accepts, and the <c>payload_press</c> it advertises.</summary>
    public const string DefaultPress = "PRESS";

    public override string Platform => "button";

    public override bool HasState => false;

    /// <summary>Never reached: a button declares no state topic, so there is nothing to say an
    /// absent reading on.</summary>
    public override string? NoValuePayload => null;

    /// <summary>What the press does.</summary>
    public required Func<MqttCommandVerdict> Press { get; init; }

    public string PayloadPress { get; init; } = DefaultPress;

    public override MqttCommandVerdict Accept(string payload) =>
        string.Equals(payload, PayloadPress, StringComparison.Ordinal)
            ? Press()
            : MqttCommandVerdict.Malformed($"Expected '{PayloadPress}'.");

    private protected override string? ReadPayload() => null;

    internal override void Describe(DiscoveryKeys keys) => keys.Set("payload_press", PayloadPress);
}
