namespace ZeroZero.Mqtt;

/// <summary>One endpoint resolved to what the wire actually needs: a host and port for TCP, a URI
/// for WebSocket, and whether the link is encrypted. Pure data, so the live publisher and the
/// connection check configure the client from the same value.</summary>
public readonly record struct MqttEndpointAddress(
    MqttTransport Transport, string Host, int Port, string Uri, bool Encrypted);

/// <summary>Turns the broker host and port into whatever the chosen transport needs. The one place
/// either transport is spelled out, so the live publisher and the connection check cannot drift
/// apart.</summary>
/// <remarks>
/// TCP takes the host and port as given. WebSocket takes a URI, so the same pair is folded into one:
/// the port picks the authority and, with the encryption switch, the scheme. A host typed with a
/// <c>ws://</c> or <c>wss://</c> scheme is honoured exactly as written, which covers a broker behind
/// a path the port alone cannot express.
/// </remarks>
public static class MqttEndpoint
{
    /// <summary>Ports whose WebSocket listener is served over TLS by convention. A bare host on one
    /// of them resolves to <c>wss</c> without the encryption switch having to be found first — a
    /// broker published through a CDN or reverse proxy is only ever reachable that way.</summary>
    private static readonly int[] SecureWebSocketPorts = [443, 8084, 8883];

    /// <summary>Whether the link will actually be encrypted, as opposed to whether encryption was
    /// asked for. The two differ on a WebSocket port whose scheme is fixed by convention: the address
    /// decides there, and no setting can undo it. Pure, and the one place the difference is worked
    /// out, so the plan, the memory and what a status line says cannot disagree about it.</summary>
    public static bool Encrypts(MqttTransport transport, int port, bool requested) =>
        requested || (transport == MqttTransport.WebSocket && SecureWebSocketPorts.Contains(ClampPort(port)));

    /// <summary>The URI the WebSocket transport connects to. Pure.</summary>
    public static string WebSocketUri(string host, int port, bool useTls)
    {
        string trimmed = (host ?? "").Trim();
        if (HasWebSocketScheme(trimmed)) return trimmed;

        int    resolved = ClampPort(port);
        bool   secure   = useTls || SecureWebSocketPorts.Contains(resolved);
        string scheme   = secure ? "wss" : "ws";
        // The scheme's own default port is left off, so the common case reads as the plain host it is.
        int implied = secure ? 443 : 80;
        return resolved == implied ? $"{scheme}://{trimmed}" : $"{scheme}://{trimmed}:{resolved}";
    }

    /// <summary>Host and port a plain socket must open before any MQTT is spoken — the stage that
    /// makes "nothing is listening" a verdict from the OS rather than a library exception. Pure.</summary>
    public static (string Host, int Port) Reachability(
        string host, int port, MqttTransport transport, bool useTls)
    {
        if (transport == MqttTransport.Tcp) return ((host ?? "").Trim(), ClampPort(port));

        // Uri knows ws/wss default to 80/443, so an authority without a port resolves on its own.
        return Uri.TryCreate(WebSocketUri(host, port, useTls), UriKind.Absolute, out var uri)
            ? (uri.Host, uri.Port)
            : ((host ?? "").Trim(), ClampPort(port));
    }

    /// <summary>One candidate against one host, resolved to the wire. Pure, and free of any client
    /// library: the wiring is applied from this rather than composed alongside it.</summary>
    public static MqttEndpointAddress Resolve(string host, MqttEndpointCandidate candidate)
    {
        var (reachHost, reachPort) =
            Reachability(host, candidate.Port, candidate.Transport, candidate.Encrypted);

        if (candidate.Transport == MqttTransport.Tcp)
            return new(MqttTransport.Tcp, reachHost, reachPort, "", candidate.Encrypted);

        string uri = WebSocketUri(host, candidate.Port, candidate.Encrypted);
        bool encrypted = uri.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
        return new(MqttTransport.WebSocket, reachHost, reachPort, uri, encrypted);
    }

    private static bool HasWebSocketScheme(string host) =>
        host.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
        host.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);

    // Clamped rather than rejected: a hand-edited settings file reaches this unchecked, and the
    // client library throws on a port outside the protocol's range.
    private static int ClampPort(int port) => Math.Clamp(port, 1, 65535);
}
