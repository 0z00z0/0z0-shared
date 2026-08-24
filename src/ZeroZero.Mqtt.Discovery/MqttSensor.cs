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

    /// <summary>A receiver ignores an empty payload on a sensor and keeps the value it already has —
    /// on a sensor with no device class it stores the empty string instead — so an absent reading is
    /// published as <see cref="MqttPayload.None"/>.</summary>
    public override string? NoValuePayload => MqttPayload.None;

    /// <summary>The current reading, or null when there is none.</summary>
    public required Func<string?> Read { get; init; }

    /// <summary>The unit the reading is in, in the receiver's own vocabulary.</summary>
    public string? Unit { get; init; }

    public MqttStateClass StateClass { get; init; }

    /// <summary>How many decimals the receiver displays. Null leaves it the choice.</summary>
    public int? DisplayPrecision { get; init; }

    /// <summary>Whether the receiver writes a new state even when the payload has not changed, so
    /// last-changed tracks every publish rather than every change.</summary>
    public bool ForceUpdate { get; init; }

    /// <summary>How long a reading stays valid before the receiver marks the entity unavailable. Null
    /// leaves it valid indefinitely.</summary>
    /// <remarks>Pair it with <see cref="MqttEntity.Retain"/> set false. A retained value is replayed
    /// on every subscribe, so an expiry that already elapsed comes back looking current.</remarks>
    public int? ExpireAfter { get; init; }

    private protected override string? ReadPayload() => Read();

    internal override void Describe(DiscoveryKeys keys)
    {
        keys.Set("unit_of_measurement", Unit);
        keys.Set("state_class", StateClassKey(StateClass));
        keys.Set("suggested_display_precision", DisplayPrecision);
        keys.SetWhenTrue("force_update", ForceUpdate);
        keys.Set("expire_after", ExpireAfter);
    }

    private static string? StateClassKey(MqttStateClass stateClass) => stateClass switch
    {
        MqttStateClass.Measurement => "measurement",
        MqttStateClass.Total => "total",
        MqttStateClass.TotalIncreasing => "total_increasing",
        _ => null,
    };
}
