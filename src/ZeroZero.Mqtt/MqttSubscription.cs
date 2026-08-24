namespace ZeroZero.Mqtt;

/// <summary>One message as it arrived.</summary>
public readonly record struct MqttInboundMessage(string Topic, string Payload, bool Retained);

/// <summary>A subscription outside the command tree, with its handler. The commands have their own
/// wildcard and their own router; this is for everything else a layer above needs to hear — a
/// receiver's birth message, most obviously.</summary>
/// <param name="TopicFilter">The filter to subscribe. Wildcards are the broker's to interpret;
/// <see cref="Matches"/> is what decides whether an arriving message belongs to this
/// subscription.</param>
/// <param name="Handler">Run on the command worker, one message at a time, so a slow handler cannot
/// stall the client's receive callback.</param>
public sealed record MqttSubscription(
    string TopicFilter,
    Func<MqttInboundMessage, CancellationToken, Task> Handler,
    MqttQos Qos = MqttQos.AtLeastOnce)
{
    /// <summary>Whether a topic belongs to this subscription. Pure, and the module's own match
    /// rather than the broker's, because a client subscribed to several filters is handed every
    /// message on one callback with nothing saying which filter brought it.</summary>
    public bool Matches(string topic) => MatchesFilter(TopicFilter, topic);

    /// <summary>MQTT topic-filter matching: <c>+</c> stands for one level, <c>#</c> for the rest.
    /// Pure.</summary>
    public static bool MatchesFilter(string filter, string topic)
    {
        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');

        for (int i = 0; i < filterLevels.Length; i++)
        {
            // '#' takes the remainder, and the protocol allows it only as the last level.
            if (filterLevels[i] == "#") return i == filterLevels.Length - 1 && i <= topicLevels.Length;
            if (i >= topicLevels.Length) return false;
            if (filterLevels[i] == "+") continue;
            if (!string.Equals(filterLevels[i], topicLevels[i], StringComparison.Ordinal)) return false;
        }

        return filterLevels.Length == topicLevels.Length;
    }
}
