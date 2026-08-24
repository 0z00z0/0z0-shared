using System.Text.Json.Serialization;

namespace ZeroZero.Mqtt;

/// <summary>How the client reaches the broker. Not two dialects of one endpoint: a broker that
/// serves both listens on separate ports, and which one is reachable depends on where the machine
/// sits — plain TCP on the internal network, WebSocket through whatever fronts it from outside.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MqttTransport { Tcp, WebSocket }

/// <summary>The user's choice. <see cref="Auto"/> is a probe order rather than a third transport,
/// so an explicit choice is never second-guessed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MqttTransportMode { Auto, Tcp, WebSocket }

/// <summary>Whether the link to the broker is encrypted. Three-valued for the same reason the port
/// and the transport are: which one a broker accepts is a property of the broker, so it is something
/// to find rather than something to know. <see cref="Auto"/> is a probe order, not a third kind of
/// link — encrypted first, then plain — and an explicit choice is never probed around.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MqttEncryptionMode { Auto, On, Off }
