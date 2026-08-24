namespace ZeroZero.Mqtt.Discovery;

/// <summary>A choice from a list the receiver can also write.</summary>
/// <remarks>
/// <para>The option list is a function, not a fixed array: a list composed from what the machine
/// currently holds changes while the application runs, and a changed list is a changed announcement.
/// It is read on every pass.</para>
/// <para>A select never publishes an empty payload — a receiver ignores one and goes on offering the
/// last option it saw. An absent reading arrives as <see cref="MqttPayload.None"/>, which resets the
/// selection and is deliberately not one of <see cref="Options"/>: the receiver accepts it as a reset
/// without it having to be offered.</para>
/// </remarks>
public sealed class MqttSelect : MqttCommandEntity
{
    public override string Platform => "select";

    /// <summary>The reset literal. Not an option, so the picker offers only real choices.</summary>
    public override string? NoValuePayload => MqttPayload.None;

    /// <summary>The options as they stand. Read on every announcement pass.</summary>
    public required Func<IReadOnlyList<string>> Options { get; init; }

    /// <summary>The option currently in force, or null when there is none.</summary>
    public required Func<string?> Read { get; init; }

    /// <summary>What to do with an inbound option that is one of the current ones.</summary>
    public required Func<string, MqttCommandVerdict> Apply { get; init; }

    public override MqttCommandVerdict Accept(string payload)
    {
        // The reset literal is a reading the module publishes, not a request: there is nothing to
        // apply. Guarded here because anything holding a broker connection can send it.
        if (string.Equals(payload, MqttPayload.None, StringComparison.Ordinal))
            return MqttCommandVerdict.NotAnOption($"'{MqttPayload.None}' stands for no current value.");

        return Options().Contains(payload, StringComparer.Ordinal)
            ? Apply(payload)
            : MqttCommandVerdict.NotAnOption($"'{payload}' is not one of the current options.");
    }

    private protected override string? ReadPayload() => Read();

    internal override void Describe(DiscoveryKeys keys) => keys.SetList("options", Options());
}
