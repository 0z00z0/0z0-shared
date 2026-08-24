namespace ZeroZero.Mqtt;

/// <summary>The MQTT versions the module can speak, numbered as the protocol numbers them.</summary>
public enum MqttProtocolVersion
{
    /// <summary>MQTT 3.1.1. No reason codes on a PUBACK, and only a five-value CONNACK.</summary>
    V311 = 4,

    /// <summary>MQTT 5.0.</summary>
    V500 = 5,
}

/// <summary>The version every connection and every probe speaks, in one place.</summary>
/// <remarks>
/// Pinned rather than left to the library's default, because the version is what decides whether a
/// refusal can be read at all: 5.0 answers a QoS 1 publish with a reason code, so a message the
/// broker declined is distinguishable from one it accepted, and its CONNACK separates "bad
/// credentials" from "refused for another reason". Under 3.1.1 both collapse, and a failed publish
/// would be recorded as sent.
/// </remarks>
public static class MqttProtocol
{
    public static MqttProtocolVersion Version => MqttProtocolVersion.V500;
}
