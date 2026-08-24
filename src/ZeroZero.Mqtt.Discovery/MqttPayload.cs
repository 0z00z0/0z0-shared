using System.Globalization;

namespace ZeroZero.Mqtt.Discovery;

/// <summary>The plain values a bare state topic carries, and the parse back. Pure.</summary>
/// <remarks>Every payload here is machine-readable, so numbers format and parse with
/// <see cref="CultureInfo.InvariantCulture"/> — a decimal comma on the wire would be read as a
/// thousands separator by a receiver in another locale, or not at all.</remarks>
public static class MqttPayload
{
    /// <summary>The boolean payloads the discovery layer declares and publishes.</summary>
    public const string On = "ON";

    public const string Off = "OFF";

    /// <summary>The payload that says there is no reading. A receiver ignores a zero-length payload on
    /// sensor, binary sensor, switch, number and select and goes on showing the last value it saw, so
    /// an absent reading has to arrive as this literal.</summary>
    /// <remarks>It collides with a text-valued sensor whose genuine reading is the word <c>None</c>:
    /// that reading and no reading at all are the same bytes. Unavoidable — the receiver reserves the
    /// literal and offers no second form.</remarks>
    public const string None = "None";

    /// <summary>A boolean reading, or null when there is none.</summary>
    public static string? Flag(bool? value) => value switch
    {
        true => On,
        false => Off,
        null => null,
    };

    /// <summary>A numeric reading, or null when there is none. Shortest round-trippable form.</summary>
    public static string? Number(double? value) =>
        value is { } v && !double.IsNaN(v) && !double.IsInfinity(v)
            ? v.ToString(CultureInfo.InvariantCulture)
            : null;

    /// <summary>A numeric reading from an integer, for the common case.</summary>
    public static string? Number(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    /// <summary>An inbound boolean, or null when the payload is neither the on nor the off form.
    /// Case-insensitive: a hand-typed <c>on</c> from a shell script is the same request.</summary>
    public static bool? ReadFlag(string payload, string on = On, string off = Off)
    {
        if (string.Equals(payload, on, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(payload, off, StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    /// <summary>An inbound number, or null when the payload does not parse as one.</summary>
    public static double? ReadNumber(string payload) =>
        double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
}
