using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZeroZero.Mqtt.Discovery;

/// <summary>The one retained payload that describes a whole device. Pure — no client, no clock — so
/// the exact bytes a consumer's entity table produces can be asserted without a broker.</summary>
/// <remarks>
/// <para>One document rather than one config per component: the device block, the origin block and
/// the availability keys are written once at the root and inherited, and a set of several dozen
/// entities is announced in a single retained publish.</para>
/// <para>A component is removed by writing it with only its platform key. Leaving it out of a later
/// document does not remove it — the receiver keeps what it already has — so removal is something the
/// document says, not something it omits. Removal is permanent: it takes the receiver's registry entry
/// with it, and with that the name, the entity id and the area the user chose. Only an entity the
/// entity table no longer contains is written that way; one that is merely not being published now
/// keeps its whole entry and is pointed at an availability topic that says offline.</para>
/// </remarks>
public static class DiscoveryDocument
{
    // Not an HTML context, so the default escaping buys nothing and costs legibility: a device named
    // in anything but ASCII is unreadable to anyone watching the topic.
    private static readonly JsonSerializerOptions Writer =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The document for one pass.</summary>
    /// <param name="components">The entities this configuration announces.</param>
    /// <param name="withheld">Entities the record says were announced and this configuration is not
    /// publishing right now — a group switched off, an <see cref="MqttEntity.Include"/> gone false.
    /// Written whole, plus their own availability topic, so the receiver shows them unavailable and
    /// every registry setting survives the entity coming back.</param>
    /// <param name="removed">Entities the record says were announced and the entity table no longer
    /// contains: each written with only its platform key, which is what deletes it.</param>
    public static string Build(
        string topicRoot,
        MqttDeviceIdentity identity,
        DiscoveryDevice device,
        DiscoveryOrigin origin,
        IReadOnlyList<MqttEntity> components,
        IReadOnlyList<MqttEntity> withheld,
        IReadOnlyList<PublishedEntity> removed,
        string onlinePayload = "online",
        string offlinePayload = "offline")
    {
        var root = new JsonObject
        {
            ["dev"] = DeviceBlock(identity, device),
            ["o"] = OriginBlock(origin),
            // Once, at the root. No component repeats it, except one that is being withheld.
            ["availability_topic"] = MqttTopics.Availability(topicRoot, identity.DeviceId),
            ["payload_available"] = onlinePayload,
            ["payload_not_available"] = offlinePayload,
            ["qos"] = (int)MqttQos.AtLeastOnce,
        };

        string withheldTopic = MqttTopics.WithheldAvailability(topicRoot, identity.DeviceId);
        var entries = new JsonObject();
        foreach (var entity in components)
            entries[entity.EntityId] = Component(topicRoot, identity.DeviceId, entity, null);
        foreach (var entity in withheld)
            entries[entity.EntityId] = Component(topicRoot, identity.DeviceId, entity, withheldTopic);
        foreach (var entity in removed)
            entries[entity.EntityId] = new JsonObject { ["p"] = entity.Platform };

        root["cmps"] = entries;
        return root.ToJsonString(Writer);
    }

    /// <param name="availabilityTopic">An availability topic of the component's own, or null to
    /// inherit the root's. Non-null only for a withheld entity, whose topic retains the offline
    /// payload for as long as it is withheld.</param>
    private static JsonObject Component(
        string topicRoot, string deviceId, MqttEntity entity, string? availabilityTopic)
    {
        var entry = new JsonObject
        {
            ["p"] = entity.Platform,
            // The registry key, and what carries the entity across any change to its topics. There is
            // deliberately no object_id and no default_entity_id: both pin an entity id the receiver
            // is better left to compose, and the first is deprecated under device discovery.
            ["unique_id"] = $"{deviceId}_{entity.EntityId}",
        };

        var keys = new DiscoveryKeys(entry);
        // Null is a value here, not an omission: it makes the entity the device's main feature.
        keys.SetOrNull("name", entity.Name);

        if (entity.HasState)
            entry["state_topic"] = MqttTopics.Channel(topicRoot, deviceId, entity.EntityId);
        if (entity.IsCommand)
            entry["command_topic"] = MqttTopics.Command(topicRoot, deviceId, entity.EntityId);

        // Overrides the root's, which is the whole of how a withheld entity is shown unavailable
        // without its registry entry being touched.
        keys.Set("availability_topic", availabilityTopic);

        // Primary is a value, not a gap: it is what keeps a control on the main card.
        keys.Set("entity_category", CategoryKey(entity.Category));
        keys.Set("icon", entity.Icon);
        keys.Set("device_class", entity.DeviceClass);
        keys.SetWhenFalse("enabled_by_default", entity.EnabledByDefault);
        entity.Describe(keys);

        if (entity.Extra is { } extra)
            foreach (var (key, value) in extra)
                keys.SetRaw(key, value);

        return entry;
    }

    private static JsonObject DeviceBlock(MqttDeviceIdentity identity, DiscoveryDevice device)
    {
        var block = new JsonObject
        {
            ["ids"] = new JsonArray(identity.DeviceId),
            ["name"] = identity.DeviceName,
            ["mf"] = device.Manufacturer,
            ["mdl"] = device.Model,
            ["sw"] = device.SoftwareVersion,
        };
        var keys = new DiscoveryKeys(block);
        keys.Set("hw", device.HardwareVersion);
        keys.Set("sn", device.SerialNumber);
        keys.Set("cu", device.ConfigurationUrl);
        return block;
    }

    private static JsonObject OriginBlock(DiscoveryOrigin origin)
    {
        var block = new JsonObject
        {
            ["name"] = origin.Name,
            ["sw"] = origin.SoftwareVersion,
        };
        if (origin.SupportUrl is { Length: > 0 } url) block["url"] = url;
        return block;
    }

    private static string? CategoryKey(MqttEntityCategory category) => category switch
    {
        MqttEntityCategory.Config => "config",
        MqttEntityCategory.Diagnostic => "diagnostic",
        _ => null,
    };
}
