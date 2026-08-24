namespace ZeroZero.Mqtt;

/// <summary>What the connection is doing, as a status line says it.</summary>
public enum MqttConnectionState
{
    /// <summary>Publishing is off, or no broker host is set. Nothing touches the network.</summary>
    Disabled,

    /// <summary>Working through a sweep of candidate endpoints: more than one is on offer, so which
    /// one answers is still being found.</summary>
    Searching,

    /// <summary>Trying the one endpoint there is — pinned by hand, or remembered from last time.</summary>
    Connecting,

    /// <summary>Connected, announced and subscribed.</summary>
    Connected,

    /// <summary>Nothing answered; waiting out the backoff before the next round.</summary>
    Retrying,

    /// <summary>The broker answered and refused: wrong credentials, or a refusal of its own. Waiting
    /// will not change it, so a status line says so rather than showing a spinner for ever.</summary>
    Failed,
}
