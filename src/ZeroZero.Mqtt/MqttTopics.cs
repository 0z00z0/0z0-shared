namespace ZeroZero.Mqtt;

/// <summary>The single place a topic string is composed. Pure.</summary>
/// <remarks>
/// Everything the module publishes sits under <c>&lt;topicRoot&gt;/&lt;deviceId&gt;/</c>. The topic
/// root is the application's, supplied once on <see cref="MqttConnectionSetup"/>; nothing here names
/// a product.
/// </remarks>
public static class MqttTopics
{
    /// <summary>The channel key the availability topic occupies. A declared channel may not use it —
    /// the will and the state would then contend for one topic.</summary>
    public const string AvailabilityKey = "availability";

    /// <summary>The segment every command topic sits under.</summary>
    public const string CommandSegment = "cmd";

    /// <summary>One published channel's retained topic, e.g. <c>appname/&lt;device&gt;/cpu_load</c>.</summary>
    public static string Channel(string topicRoot, string deviceId, string channelKey) =>
        $"{topicRoot}/{deviceId}/{channelKey}";

    public static string Availability(string topicRoot, string deviceId) =>
        Channel(topicRoot, deviceId, AvailabilityKey);

    /// <summary>The command topic for one command entity, e.g. <c>appname/&lt;device&gt;/cmd/quiet_mode</c>.</summary>
    public static string Command(string topicRoot, string deviceId, string entityId) =>
        $"{topicRoot}/{deviceId}/{CommandSegment}/{entityId}";

    /// <summary>The one wildcard subscription that covers every command entity.</summary>
    public static string CommandFilter(string topicRoot, string deviceId) =>
        $"{topicRoot}/{deviceId}/{CommandSegment}/#";

    /// <summary>The entity id parsed out of a full command topic, or null if it isn't one.</summary>
    public static string? CommandEntityId(string topicRoot, string deviceId, string topic)
    {
        string prefix = $"{topicRoot}/{deviceId}/{CommandSegment}/";
        return topic.StartsWith(prefix, StringComparison.Ordinal) && topic.Length > prefix.Length
            ? topic[prefix.Length..]
            : null;
    }

    /// <summary>Why a channel key cannot be published on, or null when it can. A key carrying a
    /// separator or a wildcard would silently publish somewhere other than where the topic string
    /// says, and a wildcard in a published topic is rejected by the broker outright.</summary>
    public static string? ValidateChannelKey(string key) => key switch
    {
        null or "" => "A channel key cannot be empty.",
        AvailabilityKey => $"'{AvailabilityKey}' is the availability topic and cannot be a channel.",
        _ when key.AsSpan().ContainsAny('/', '+', '#') =>
            "A channel key cannot contain '/', '+' or '#'.",
        _ => null,
    };
}
