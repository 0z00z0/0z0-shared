namespace ZeroZero.Mqtt.Discovery;

/// <summary>Which control a receiver draws for a number. <see cref="Auto"/> writes no <c>mode</c>
/// and leaves it the choice.</summary>
public enum MqttNumberMode { Auto, Box, Slider }

/// <summary>A bounded number the receiver can also write.</summary>
/// <remarks>The bounds are declared once and enforced twice: the receiver keeps its own control
/// inside them, and <see cref="Accept"/> refuses anything outside them, because a payload can arrive
/// from anything holding a broker connection.</remarks>
public sealed class MqttNumber : MqttCommandEntity
{
    public override string Platform => "number";

    /// <summary>A receiver ignores an empty payload here and keeps the value it already has, so an
    /// absent reading is published as <see cref="MqttPayload.None"/>.</summary>
    public override string? NoValuePayload => MqttPayload.None;

    /// <summary>The smallest step the receiver's schema accepts. A smaller one drops the whole
    /// component from the document with nothing to see locally.</summary>
    public const double MinimumStep = 0.001;

    /// <summary>The current reading, or null when there is none.</summary>
    public required Func<double?> Read { get; init; }

    /// <summary>What to do with an inbound value that parsed and is within the bounds.</summary>
    public required Func<double, MqttCommandVerdict> Apply { get; init; }

    public required double Min { get; init; }

    public required double Max { get; init; }

    public double Step { get; init; } = 1;

    /// <summary>The unit the value is in, in the receiver's own vocabulary.</summary>
    public string? Unit { get; init; }

    public MqttNumberMode Mode { get; init; }

    public override MqttCommandVerdict Accept(string payload)
    {
        if (MqttPayload.ReadNumber(payload) is not { } value)
            return MqttCommandVerdict.Malformed("Expected a number.");

        return value < Min || value > Max
            ? MqttCommandVerdict.OutOfRange($"Expected {MqttPayload.Number(Min)} to {MqttPayload.Number(Max)}.")
            : Apply(value);
    }

    internal override string? Validate() => this switch
    {
        _ when Max < Min => $"Entity '{EntityId}' declares a maximum below its minimum.",
        _ when !(Step >= MinimumStep) =>
            $"Entity '{EntityId}' declares a step below {MqttPayload.Number(MinimumStep)}, which the receiver's schema rejects.",
        _ => base.Validate(),
    };

    private protected override string? ReadPayload() => MqttPayload.Number(Read());

    internal override void Describe(DiscoveryKeys keys)
    {
        keys.Set("min", Min);
        keys.Set("max", Max);
        keys.Set("step", Step);
        keys.Set("unit_of_measurement", Unit);
        keys.Set("mode", ModeKey(Mode));
    }

    private static string? ModeKey(MqttNumberMode mode) => mode switch
    {
        MqttNumberMode.Box => "box",
        MqttNumberMode.Slider => "slider",
        _ => null,
    };
}
