namespace ZeroZero.Mqtt.Discovery;

/// <summary>How a receiver draws a text field.</summary>
public enum MqttTextMode { Text, Password }

/// <summary>A string the receiver can also write.</summary>
public sealed class MqttText : MqttCommandEntity
{
    /// <summary>The receiver's own ceiling on a text payload.</summary>
    public const int MaxLengthCeiling = 255;

    public override string Platform => "text";

    /// <summary>The one platform that keeps null. An empty payload is a legitimate value here — the
    /// empty string — so a text entity with no reading empties its topic, and the two are
    /// indistinguishable on the wire. <see cref="MqttPayload.None"/> is not used because it would be
    /// stored as the four-character word rather than read as "no value". A consumer that needs the two
    /// apart declares a sentinel of its own through <see cref="MqttEntity.Extra"/> and never returns
    /// null.</summary>
    public override string? NoValuePayload => null;

    /// <summary>The current value, or null when there is none.</summary>
    public required Func<string?> Read { get; init; }

    /// <summary>What to do with an inbound value within the declared length.</summary>
    public required Func<string, MqttCommandVerdict> Apply { get; init; }

    public int MinLength { get; init; }

    public int MaxLength { get; init; } = MaxLengthCeiling;

    public MqttTextMode Mode { get; init; }

    /// <summary>A regular expression the receiver validates against before sending. Never the only
    /// guard: <see cref="Accept"/> still judges what arrives.</summary>
    public string? Pattern { get; init; }

    public override MqttCommandVerdict Accept(string payload) =>
        payload.Length < MinLength || payload.Length > MaxLength
            ? MqttCommandVerdict.OutOfRange($"Expected {MinLength} to {MaxLength} characters.")
            : Apply(payload);

    internal override string? Validate() => MaxLength switch
    {
        < 0 or > MaxLengthCeiling =>
            $"Entity '{EntityId}' declares a maximum length outside 0 to {MaxLengthCeiling}.",
        _ when MaxLength < MinLength =>
            $"Entity '{EntityId}' declares a maximum length below its minimum.",
        _ => base.Validate(),
    };

    private protected override string? ReadPayload() => Read();

    internal override void Describe(DiscoveryKeys keys)
    {
        keys.Set("min", MinLength);
        keys.Set("max", MaxLength);
        keys.Set("mode", Mode == MqttTextMode.Password ? "password" : null);
        keys.Set("pattern", Pattern);
    }
}
