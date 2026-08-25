namespace ZeroZero.Mqtt;

/// <summary>The reconnect delay and the session it is judged against: the escalation after a failed
/// round, the reset once a session has proven stable, and the flap that escalates instead of
/// resetting. Reads no clock — every transition is handed the instant — so a whole sequence is
/// assertable without waiting one out.</summary>
/// <remarks>Holds what the maintain loop would otherwise keep in two locals. The arithmetic and the
/// flap window stay on <see cref="MqttConnection"/>, which owns the policy; this owns the state that
/// policy is applied to.</remarks>
internal sealed class MqttReconnectBackoff
{
    private DateTimeOffset? _connectedSince;

    /// <summary>What the next wait costs while the connection is down.</summary>
    public TimeSpan Delay { get; private set; } = MqttConnection.InitialBackoff;

    /// <summary>Forgets the session and drops back to the floor. A resume from standby killed the
    /// socket, so the session that ended with it is not a flap.</summary>
    public void Resume()
    {
        _connectedSince = null;
        Delay = MqttConnection.InitialBackoff;
    }

    /// <summary>Consumes the ended session ahead of a reconnect: the escalated delay to wait out when
    /// it died young, or null to retry at once — a session that lasted, or none at all.</summary>
    public TimeSpan? BeforeConnect(DateTimeOffset now)
    {
        bool flapped = _connectedSince is { } since && !MqttConnection.IsStableConnection(now - since);
        _connectedSince = null;
        if (!flapped) return null;

        Delay = MqttConnection.NextBackoff(Delay);
        return Delay;
    }

    /// <summary>Starts a live session at <paramref name="now"/>.</summary>
    public void Connected(DateTimeOffset now) => _connectedSince = now;

    /// <summary>Lengthens the wait after a round that connected nowhere, and after a connect sequence
    /// that threw with the socket up.</summary>
    public void Failed()
    {
        _connectedSince = null;
        Delay = MqttConnection.NextBackoff(Delay);
    }

    /// <summary>Drops back to the floor once the live session has outlived the flap window, so the
    /// next genuine drop reconnects fast.</summary>
    public void SettleIfStable(DateTimeOffset now)
    {
        if (_connectedSince is { } since && MqttConnection.IsStableConnection(now - since))
            Delay = MqttConnection.InitialBackoff;
    }
}
