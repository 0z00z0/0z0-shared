namespace ZeroZero.Mqtt.Discovery;

/// <summary>How a receiver treats a numeric series over time. <see cref="None"/> writes no
/// <c>state_class</c>, which is right for anything that is not a measurement — a text reading, a
/// version string, a name.</summary>
public enum MqttStateClass { None, Measurement, Total, TotalIncreasing }

/// <summary>A read-only value. The one component whose payload is not typed by the platform: a
/// sensor carries a number, a duration or a word with equal standing, so its reader returns the
/// payload itself.</summary>
/// <remarks>A numeric reading goes through <see cref="MqttPayload.Number(double?)"/> so it is
/// formatted for a machine rather than for the current locale.</remarks>
public sealed class MqttSensor : MqttEntity
{
    public override string Platform => "sensor";

    /// <summary>An empty payload reads as no value on a sensor, so an absent reading empties the
    /// topic rather than leaving a value of unknown age standing.</summary>
    public override string? NoValuePayload => null;

    /// <summary>The current reading, or null when there is none.</summary>
    public required Func<string?> Read { get; init; }

    /// <summary>The unit the reading is in, in the receiver's own vocabulary.</summary>
    public string? Unit { get; init; }

    public MqttStateClass StateClass { get; init; }

    /// <summary>How many decimals the receiver displays. Null leaves it the choice.</summary>
    public int? DisplayPrecision { get; init; }

    private protected override string? ReadPayload() => Read();

    internal override void Describe(DiscoveryKeys keys)
    {
        keys.Set("unit_of_measurement", Unit);
        keys.Set("state_class", StateClassKey(StateClass));
        keys.Set("suggested_display_precision", DisplayPrecision);
    }

    private static string? StateClassKey(MqttStateClass stateClass) => stateClass switch
    {
        MqttStateClass.Measurement => "measurement",
        MqttStateClass.Total => "total",
        MqttStateClass.TotalIncreasing => "total_increasing",
        _ => null,
    };
}
