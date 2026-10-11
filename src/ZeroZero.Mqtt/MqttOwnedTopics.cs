namespace ZeroZero.Mqtt;

/// <summary>Every topic one <see cref="MqttConnection"/> owns under a device id, and the superseded
/// ids whose topics are still to be emptied.</summary>
internal sealed class MqttOwnedTopics(
    MqttConnectionSetup setup, IMqttPublisher publisher, Func<bool> isConnected, Func<string> deviceIdInForce)
{
    /// <summary>A superseded identity with the keys it owned when superseded: read later, the keys
    /// in force would compose the old id's topics from the new id's entities.</summary>
    internal sealed record RetiredIdentity(
        MqttDeviceIdentity Identity, IReadOnlyList<string> Channels, IReadOnlyList<string> Commands);

    // Held, not evicted inline, because the change can land while disconnected. Keyed by device id,
    // so A → B → C offline keeps A and B; the id in force is never held, so A → B → A is safe.
    private readonly Lock _retiredLock = new();
    private readonly Dictionary<string, RetiredIdentity> _retired = new(StringComparer.Ordinal);

    /// <summary>Empties every retained topic each superseded identity owned, so a consumer deletes the
    /// old device rather than leaving a ghost. No-op while disconnected: the next connect runs it. An
    /// eviction that did not land is held again, since nothing else still knows the old id.</summary>
    public async Task ClearRetiredIdentityAsync(CancellationToken ct)
    {
        if (!isConnected()) return;

        List<RetiredIdentity> retired;
        lock (_retiredLock)
        {
            if (_retired.Count == 0) return;
            retired = [.. _retired.Values];
            _retired.Clear();
        }

        for (int i = 0; i < retired.Count; i++)
        {
            bool landed;
            try
            {
                if (setup.Listener is { } listener)
                    await listener.OnIdentityRetiredAsync(publisher, retired[i].Identity, ct).ConfigureAwait(false);

                landed = await publisher.PublishAsync(
                        OwnTopics(retired[i].Identity.DeviceId, retired[i].Channels, retired[i].Commands)
                            .Select(MqttMessage.Empty),
                        ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                foreach (var left in retired.Skip(i)) HoldRetired(left);
                throw;
            }

            if (!landed) HoldRetired(retired[i]);
        }
    }

    /// <summary>Records a superseded identity for eviction. A repeat id merges its keys; the id in
    /// force is dropped rather than held, since evicting it would empty the live device.</summary>
    public void HoldRetired(RetiredIdentity retired)
    {
        lock (_retiredLock)
        {
            string id = retired.Identity.DeviceId;
            if (_retired.TryGetValue(id, out var held))
                retired = new(
                    retired.Identity,
                    [.. held.Channels.Union(retired.Channels, StringComparer.Ordinal)],
                    [.. held.Commands.Union(retired.Commands, StringComparer.Ordinal)]);
            _retired[id] = retired;
            _retired.Remove(deviceIdInForce());
        }
    }

    /// <summary>The two availability topics, each channel and each command topic under one id. The
    /// keys are passed in: the set in force belongs to the new id. Command topics are included because
    /// something else can leave a retained command there, redelivered once the id publishes again.</summary>
    public IEnumerable<string> OwnTopics(
        string deviceId, IReadOnlyList<string> channels, IReadOnlyList<string> commands)
    {
        yield return MqttTopics.Availability(setup.TopicRoot, deviceId);
        yield return MqttTopics.WithheldAvailability(setup.TopicRoot, deviceId);
        foreach (string key in channels)
            yield return MqttTopics.Channel(setup.TopicRoot, deviceId, key);
        foreach (string entityId in commands)
            yield return MqttTopics.Command(setup.TopicRoot, deviceId, entityId);
    }
}
