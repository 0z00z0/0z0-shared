namespace ZeroZero.Mqtt;

/// <summary>The module's own quality-of-service enum, so MQTTnet stays an implementation detail
/// rather than a public dependency. The values are the protocol's own.</summary>
public enum MqttQos { AtMostOnce = 0, AtLeastOnce = 1, ExactlyOnce = 2 }

/// <summary>One outbound message, so a batch can be handed over as data rather than as a sequence
/// of calls.</summary>
public readonly record struct MqttMessage(
    string Topic, string Payload, bool Retain = true, MqttQos Qos = MqttQos.AtLeastOnce)
{
    /// <summary>A zero-length retained payload: what removes a retained value from a topic.</summary>
    public static MqttMessage Empty(string topic) => new(topic, "", Retain: true);
}

/// <summary>Somewhere to publish. Taken by every collaborator, so each is testable against a
/// recording double rather than a broker.</summary>
public interface IMqttPublisher
{
    /// <summary>False when the message did not reach the broker. Only a caller the user is watching
    /// needs to know — everything else publishes into the background, where the log is the trace.</summary>
    Task<bool> PublishAsync(
        string topic, string payload, bool retain,
        MqttQos qos = MqttQos.AtLeastOnce, CancellationToken ct = default);

    /// <summary>Publishes a whole batch, overlapping the sends rather than paying a round trip each.
    /// False when any message did not reach the broker.</summary>
    /// <remarks>A group toggle, an identity eviction and a full republish all move every topic at
    /// once. At a few dozen entities the sequential form is a few dozen QoS 1 round trips, which is
    /// seconds on a remote broker.</remarks>
    Task<bool> PublishAsync(IEnumerable<MqttMessage> messages, CancellationToken ct = default);
}
