using ZeroZero.Primitives;

namespace ZeroZero.Mqtt;

/// <summary>The retained channels of one <see cref="MqttConnection"/>: reading, deduping and sending
/// each value, and emptying the topics of channels that have gone.</summary>
internal sealed class MqttChannelPublisher
{
    private readonly MqttConnectionSetup _setup;
    private readonly ILogSink _log;
    private readonly MqttChannelSet _channels;
    private readonly IMqttPublisher _publisher;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _isConnected;
    private readonly Func<bool> _clientConnected;
    private readonly Func<string> _deviceId;

    // Keys dropped while there was no link. The replace has already forgotten them, so this is the
    // only thing left that knows their values are still retained on the broker.
    private readonly Lock _pending = new();
    private readonly HashSet<string> _pendingEvictions = new(StringComparer.Ordinal);

    /// <param name="isConnected">Publishing on and the client connected.</param>
    /// <param name="clientConnected">The client connected, whatever the switch says.</param>
    public MqttChannelPublisher(
        MqttConnectionSetup setup, MqttChannelSet channels, IMqttPublisher publisher,
        Func<bool> enabled, Func<bool> isConnected, Func<bool> clientConnected, Func<string> deviceId)
    {
        _setup = setup;
        _log = setup.Log;
        _channels = channels;
        _publisher = publisher;
        _enabled = enabled;
        _isConnected = isConnected;
        _clientConnected = clientConnected;
        _deviceId = deviceId;
    }

    public void Publish(string channelKey, string payload)
    {
        if (!_enabled()) return;
        if (_channels.Find(channelKey) is not { } channel) return;
        if (!_channels.Accept(channelKey, payload)) return;
        if (!_clientConnected()) return;

        _ = SendChannelAsync(channel, Compose(channel, payload), CancellationToken.None);
    }

    public void RequestPublish(string channelKey)
    {
        if (!_enabled()) return;
        if (_channels.Signal(channelKey)) _ = Task.Run(() => ChannelLoopAsync(channelKey));
    }

    public void RequestPublish()
    {
        foreach (string key in _channels.Keys) RequestPublish(key);
    }

    /// <summary>Coalesced driver for one channel. The trailing pass is what guarantees the last
    /// snapshot wins: two concurrent reads could otherwise let an older one take the dedupe slot.</summary>
    private async Task ChannelLoopAsync(string channelKey)
    {
        do
        {
            _channels.BeginPass(channelKey);
            try
            {
                if (_channels.Find(channelKey) is { } channel)
                {
                    if (channel.Debounce > TimeSpan.Zero)
                        await Task.Delay(channel.Debounce).ConfigureAwait(false);
                    if (_enabled() && ComposeCurrent(channel, force: false) is { } message)
                        await SendChannelAsync(channel, message, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { _log.Error($"MqttConnection.Publish: {MqttProbe.Describe(ex)}", null); }
            await Task.Yield();   // never hold the pool thread across the repeat check
        }
        while (_channels.ShouldRepeat(channelKey));
    }

    public async Task<bool> PublishNowAsync()
    {
        if (!_isConnected()) return false;
        try
        {
            return await PublishChannelsAsync(_channels.Channels, force: true, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error($"MqttConnection.PublishNow: {MqttProbe.Describe(ex)}", null);
            return false;
        }
    }

    public async Task SetChannelsAsync(IEnumerable<MqttChannel> channels, CancellationToken ct = default)
    {
        var (departed, arrived) = _channels.Replace(channels);

        // Before the link is checked: the replace has already dropped these keys, so returning
        // without them written down leaves their values retained on the broker for ever.
        lock (_pending) foreach (string key in departed) _pendingEvictions.Add(key);

        // An arriving channel's topic may have been emptied when it last went, so ask for its value.
        foreach (string key in arrived) RequestPublish(key);

        await FlushEvictionsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Empties the retained topics of channels that have gone, including any that were
    /// dropped while there was no link. No-op while disconnected: the next connect runs it.</summary>
    public async Task FlushEvictionsAsync(CancellationToken ct)
    {
        if (!_isConnected()) return;

        List<string> keys;
        lock (_pending)
        {
            if (_pendingEvictions.Count == 0) return;
            keys = [.. _pendingEvictions];
            _pendingEvictions.Clear();
        }

        bool landed;
        try
        {
            landed = await _publisher.PublishAsync(
                keys.Select(key =>
                    MqttMessage.Empty(MqttTopics.Channel(_setup.TopicRoot, _deviceId(), key))),
                ct).ConfigureAwait(false);
        }
        // A cancelled batch loses the same work as a refused one: nothing else remembers these keys.
        catch { Restore(); throw; }

        // Put back what the broker did not take, so the next connect tries again.
        if (!landed) Restore();

        void Restore() { lock (_pending) foreach (string key in keys) _pendingEvictions.Add(key); }
    }

    /// <summary>Reads every channel and publishes what each one's current reading calls for, as one
    /// batch. A send that fails takes its dedupe entry with it, so the next pass re-sends.</summary>
    public async Task<bool> PublishChannelsAsync(
        IReadOnlyList<MqttChannel> channels, bool force, CancellationToken ct)
    {
        // Nothing to publish is not a failed publish: a panel would report a refusal that never was.
        if (channels.Count == 0) return true;

        var batch = new List<(MqttChannel Channel, MqttMessage Message)>(channels.Count);
        foreach (var channel in channels)
            if (ComposeCurrent(channel, force) is { } message) batch.Add((channel, message));

        if (batch.Count == 0) return false;

        bool[] results = await MqttConnection.SendConcurrentlyAsync(
            batch, entry => SendChannelAsync(entry.Channel, entry.Message, ct), ct).ConfigureAwait(false);
        return results.Any(ok => ok);
    }

    private async Task<bool> SendChannelAsync(MqttChannel channel, MqttMessage message, CancellationToken ct)
    {
        bool sent = await _publisher.PublishAsync(message.Topic, message.Payload, message.Retain, message.Qos, ct)
            .ConfigureAwait(false);

        // A payload that never arrived must not hold the dedupe slot, or the topic stays wrong.
        if (!sent) _channels.Forget(channel.Key);
        return sent;
    }

    private MqttMessage Compose(MqttChannel channel, string payload) =>
        new(MqttTopics.Channel(_setup.TopicRoot, _deviceId(), channel.Key),
            payload, channel.Retain, channel.Qos);

    /// <summary>What one channel's current reading calls for, or null when it calls for nothing. The
    /// dedupe slot is taken here, so the caller only has to send.</summary>
    private MqttMessage? ComposeCurrent(MqttChannel channel, bool force)
    {
        var (status, payload) = ReadPayload(channel);
        switch (status)
        {
            case MqttPayloadStatus.Value:
                if (force) _channels.Force(channel.Key, payload!);
                else if (!_channels.Accept(channel.Key, payload!)) return null;
                return Compose(channel, payload!);

            case MqttPayloadStatus.None:
                // Waiting for a first reading: keep what was last published, and resend it on connect.
                if (channel.RepublishLastOnConnect)
                    return force && _channels.LastPayload(channel.Key) is { Length: > 0 } last
                        ? Compose(channel, last)
                        : null;

                // Otherwise no reading is announced as the channel's own no-reading payload, once.
                return _channels.Accept(channel.Key, channel.NoValuePayload)
                    ? Compose(channel, channel.NoValuePayload)
                    : null;

            default:
                // The reader failed: what stands, stands. Emptying the topic would assert "no value".
                return null;
        }
    }

    // The application's code on the module's thread: a throw must not take the loop with it. A null
    // is "no current reading"; a throw is "the reading could not be taken".
    private (MqttPayloadStatus Status, string? Payload) ReadPayload(MqttChannel channel)
    {
        try
        {
            return channel.Payload() is { } payload
                ? (MqttPayloadStatus.Value, payload)
                : (MqttPayloadStatus.None, null);
        }
        catch (Exception ex)
        {
            _log.Error($"MqttConnection.Payload:{channel.Key}: {MqttProbe.Describe(ex)}", null);
            return (MqttPayloadStatus.Failed, null);
        }
    }
}
