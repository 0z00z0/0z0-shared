namespace ZeroZero.Mqtt;

/// <summary>Which transport, and which port, to try next. Pure: the settings, what last worked and
/// the attempts so far go in, a candidate or "nothing left to try" comes out — no client, no socket,
/// no clock.</summary>
/// <remarks>
/// Both the live publisher and the connection check walk this, so what a Test button reports is what
/// the connection will do. TCP leads in Auto because it is the internal path and the cheaper one: on
/// the internal network it answers at once and WebSocket is never attempted, while from outside it
/// costs a connect timeout per candidate before the fallback. The remembered endpoint takes that cost
/// off every later connect — a laptop that moves pays the full sweep once per move, not once per
/// reconnect — and it is remembered against the host and user it was found for, because the same
/// broker legitimately answers on a different port and transport from inside and from outside.
/// </remarks>
public static class MqttEndpointPlan
{
    /// <summary>The order the transports would be tried in from a clean slate.</summary>
    public static IReadOnlyList<MqttTransport> Order(
        MqttTransportMode setting, MqttTransport? lastSuccessful) => setting switch
    {
        // An explicit choice is the whole plan: no fallback, so a machine pinned to one path fails
        // loudly rather than quietly connecting some other way.
        MqttTransportMode.Tcp       => [MqttTransport.Tcp],
        MqttTransportMode.WebSocket => [MqttTransport.WebSocket],

        _ => lastSuccessful is MqttTransport.WebSocket
                ? [MqttTransport.WebSocket, MqttTransport.Tcp]
                : [MqttTransport.Tcp, MqttTransport.WebSocket],
    };

    /// <summary>Whether the broker itself answered, as opposed to nothing being reached. An answer
    /// ends the sweep: the same broker sits behind the next candidate too, so carrying on only spends
    /// time and blurs a precise verdict.</summary>
    /// <remarks>Neither TLS failure is an answer. Each says something about one endpoint's encryption
    /// and nothing about the next port or the other transport, so both are settled on that endpoint
    /// alone — see <see cref="DowngradeBlocked"/>.</remarks>
    public static bool Answered(MqttProbeOutcome outcome) => outcome
        is MqttProbeOutcome.Success or MqttProbeOutcome.AuthRejected or MqttProbeOutcome.Rejected;

    /// <summary>Provenance shown once the endpoint in force was found rather than chosen.</summary>
    public const string AutomaticallyDetected = "Automatically detected";

    /// <summary>Provenance shown when both halves were pinned by hand, so nothing was probed.</summary>
    public const string SetManually = "Set manually";

    /// <summary>The ports a broker is commonly reached on over one transport, most likely first.</summary>
    /// <remarks>
    /// TCP: 1883 is IANA's <c>mqtt</c> and 8883 its <c>secure-mqtt</c>, which is the whole of what a
    /// plain socket sees in practice. WebSocket is served through an HTTP front door, so its list is
    /// the front door's ports rather than MQTT's: 443 first because that is what a broker published
    /// through a CDN or reverse proxy answers on, then Mosquitto's conventional 9001, EMQX's 8083
    /// and 8084, the common alternate 8080, and finally bare 80. 80 and 8080 sit last deliberately —
    /// a CDN accepts a socket on both whether or not MQTT is behind them, so they are the candidates
    /// most likely to open and then fail the handshake.
    /// </remarks>
    public static IReadOnlyList<int> Ports(MqttTransport transport) => transport == MqttTransport.Tcp
        ? [1883, 8883]
        : [443, 9001, 8083, 8084, 8080, 80];

    /// <summary>Every port a settings dropdown offers, in the order the sweep tries them. One list
    /// for both, so what can be chosen and what is probed cannot drift apart.</summary>
    public static IReadOnlyList<int> OfferedPorts { get; } =
        [.. Ports(MqttTransport.Tcp), .. Ports(MqttTransport.WebSocket)];

    /// <summary>The port/transport pair to try after <paramref name="attempts"/>, or null when the
    /// sweep is spent. Pure: the staged settings, the memory and the attempts so far go in — no
    /// client, no socket, no clock.</summary>
    public static MqttEndpointCandidate? NextEndpoint(
        MqttEndpointRequest request, MqttEndpointMemory? memory,
        IReadOnlyList<MqttEndpointAttempt> attempts)
    {
        foreach (var attempt in attempts)
            if (Answered(attempt.Outcome)) return null;

        foreach (var candidate in Sweep(request, memory))
            if (!Attempted(attempts, candidate) && !DowngradeBlocked(attempts, candidate))
                return candidate;

        return null;
    }

    /// <summary>Whether a plain candidate must be skipped because the encrypted candidate on the same
    /// endpoint found encryption on offer. Pure.</summary>
    /// <remarks>
    /// <para>Automatic keeps its fallback to clear text, and the question is a local one about this
    /// endpoint: was a secure channel available here. Two outcomes say no and leave the downgrade
    /// open — <see cref="MqttProbeOutcome.Unreachable"/>, the operating system saying there is nothing
    /// there, and <see cref="MqttProbeOutcome.TlsUnsupported"/>, the far end taking the socket and
    /// never presenting a certificate. Neither sent a credential: the handshake fails before CONNECT
    /// does.</para>
    /// <para>Everything else blocks. A presented certificate that was not trusted means encryption
    /// <b>was</b> available, so a clear-text retry would put the password on the wire at the very
    /// broker that offered to take it in cipher; the certificate trust setting is the way out of that,
    /// not a downgrade. A rejected credential blocks for the same reason, and the sweep stops there
    /// anyway. A timeout and an unclassified failure block because neither says what was on offer.</para>
    /// </remarks>
    public static bool DowngradeBlocked(
        IReadOnlyList<MqttEndpointAttempt> attempts, MqttEndpointCandidate candidate)
    {
        if (candidate.Encrypted) return false;

        foreach (var attempt in attempts)
        {
            if (!attempt.Candidate.Encrypted) continue;
            if (attempt.Candidate.Port != candidate.Port) continue;
            if (attempt.Candidate.Transport != candidate.Transport) continue;
            if (!DowngradeSafe(attempt.Outcome)) return true;
        }

        return false;
    }

    /// <summary>Whether one encrypted attempt's outcome leaves a clear-text retry of the same endpoint
    /// open: nothing secure was on offer, and nothing secret was sent. Pure.</summary>
    public static bool DowngradeSafe(MqttProbeOutcome outcome) =>
        outcome is MqttProbeOutcome.Unreachable or MqttProbeOutcome.TlsUnsupported;

    /// <summary>Whether to ask the endpoint for encryption, in the order to ask. Pure.</summary>
    /// <remarks>
    /// Automatic always asks for encryption first and only then in clear text, per endpoint rather
    /// than per sweep — the plain retry for a port is worth more than the encrypted attempt on the
    /// next one, and holding every plain candidate back would put the ordinary internal broker
    /// behind the whole encrypted list. Nothing reorders this, not even what worked last time: the
    /// remembered endpoint leads the sweep as a whole, and within a pair cipher still comes first.
    /// Whether the plain half is reached at all is <see cref="DowngradeBlocked"/>'s business.
    /// </remarks>
    public static IReadOnlyList<bool> EncryptionOrder(MqttEncryptionMode setting) => setting switch
    {
        MqttEncryptionMode.On  => [true],
        MqttEncryptionMode.Off => [false],
        _ => [true, false],
    };

    /// <summary>Every candidate, in order, from a clean slate. Pure.</summary>
    /// <remarks>
    /// The remembered endpoint leads because it is where the broker answered last time, but it is
    /// never the whole sweep: a cached answer stops working the moment the machine moves, and one
    /// that is followed by nothing would turn a move into a permanent failure. So the full list
    /// still trails it, and the memory costs one attempt rather than the connection.
    /// </remarks>
    public static IReadOnlyList<MqttEndpointCandidate> Sweep(
        MqttEndpointRequest request, MqttEndpointMemory? memory)
    {
        var remembered = Reusable(request, memory);
        var sweep = new List<MqttEndpointCandidate>();

        void Add(MqttEndpointCandidate candidate)
        {
            if (Allowed(request, candidate) && !sweep.Contains(candidate)) sweep.Add(candidate);
        }

        // What is stored is the encryption that was actually in force, so a WebSocket port whose
        // scheme is fixed collapses the two variants into one candidate rather than doubling the
        // sweep with a duplicate URI.
        void AddEndpoint(int port, MqttTransport transport)
        {
            foreach (bool requested in EncryptionOrder(request.Encryption))
                Add(new(port, transport, MqttEndpoint.Encrypts(transport, port, requested)));
        }

        // An entry from before the encryption state was recorded only survives Reusable under an
        // explicit choice, so the setting is what fills the gap — never a bare false.
        if (remembered is { } m)
            Add(new(m.Port, m.Transport, m.Encrypted ?? MqttEndpoint.Encrypts(
                m.Transport, m.Port, request.Encryption == MqttEncryptionMode.On)));

        foreach (var transport in Order(request.Transport, remembered?.Transport))
        {
            if (request.Port is { } pinned) AddEndpoint(pinned, transport);
            else foreach (int port in Ports(transport)) AddEndpoint(port, transport);
        }

        return sweep;
    }

    /// <summary>The remembered entry that applies to this request, or null when none does. Pure.</summary>
    /// <remarks>
    /// Keyed on host and username together. The host because the same broker legitimately answers
    /// differently from inside and outside a network, so an entry found elsewhere says nothing here;
    /// the username because a broker commonly fronts separate listeners per account, and reusing one
    /// account's endpoint for another would probe the wrong door first.
    /// </remarks>
    public static MqttEndpointMemory? Reusable(MqttEndpointRequest request, MqttEndpointMemory? memory) =>
        memory is { } m
        && m.Port is >= 1 and <= 65535
        && Same(m.Host, request.Host)
        && Same(m.Username, request.Username)
        // An entry written before the encryption state was recorded cannot answer a question that
        // includes it. Absent, not false: a missing field read as plain would pin Automatic to clear
        // text for good on the strength of a struct default. Such an entry costs one sweep, once,
        // and is rewritten complete. Under an explicit choice it still applies — nothing is being
        // asked about encryption there.
        && (request.Encryption != MqttEncryptionMode.Auto || m.Encrypted is not null)
            ? m : null;

    /// <summary>Whether an explicit action should start a probe. Pure.</summary>
    /// <remarks>
    /// The trigger is the gate, not the memory: a probe costs real seconds and puts the machine on
    /// the network, so it happens because somebody asked for it. <see cref="MqttProbeTrigger"/> is
    /// the closed set of things that count as asking, and showing a settings page is deliberately not
    /// one of them. What is remembered still leads the sweep once one runs — it decides the order,
    /// never whether there is a sweep at all.
    /// </remarks>
    public static bool ShouldProbe(MqttProbeTrigger trigger, bool publishingEnabled, string host) =>
        // Nothing is probed while publishing is off: in that state the host application touches no
        // network at all, and a probe would be the one exception. A blank host has nothing to probe.
        trigger is MqttProbeTrigger.BrokerSettingChanged or MqttProbeTrigger.TestConnection
                or MqttProbeTrigger.Apply
        && publishingEnabled
        && !string.IsNullOrWhiteSpace(host);

    /// <summary>Whether the link in force is encrypted, or null when nothing has settled it yet.
    /// Pure.</summary>
    /// <remarks>
    /// What actually connected outranks what was asked for, because Automatic can end up in clear
    /// text without anyone choosing it. With nothing connected yet the setting has to answer, and
    /// where it cannot the answer is null rather than a guess — except that a pinned WebSocket port
    /// whose scheme is fixed is encrypted whatever the setting says, and saying otherwise would put
    /// a false statement about the wire on a status line.
    /// </remarks>
    public static bool? EncryptionInForce(MqttEndpointRequest request, MqttEndpointMemory? memory) =>
        Reusable(request, memory)?.Encrypted
        ?? request.Encryption switch
        {
            MqttEncryptionMode.On  => true,
            MqttEncryptionMode.Off => request.Port is { } p
                                   && request.Transport == MqttTransportMode.WebSocket
                                   && MqttEndpoint.Encrypts(MqttTransport.WebSocket, p, requested: false),
            _ => null,
        };

    /// <summary>How the endpoint in force came to be what it is, and whether it is in clear text.
    /// Pure.</summary>
    /// <remarks>The encryption clause is not decoration. Automatic falls back to plain on its own, so
    /// a link can be downgraded with no user action at all, and nothing else on a settings page would
    /// say so.</remarks>
    public static string DescribeProvenance(MqttEndpointRequest request, MqttEndpointMemory? memory)
    {
        string source = request.Port is not null
                     && request.Transport != MqttTransportMode.Auto
                     && request.Encryption != MqttEncryptionMode.Auto
            ? SetManually
            : AutomaticallyDetected;

        return EncryptionInForce(request, memory) switch
        {
            true  => $"{source} — encrypted",
            false => $"{source} — not encrypted",
            _     => source,
        };
    }

    /// <summary>A pinned port, transport or encryption is honoured exactly, remembered entry
    /// included — an explicit choice must not be reached around by something that happened to work
    /// once.</summary>
    private static bool Allowed(MqttEndpointRequest request, MqttEndpointCandidate candidate) =>
        (request.Port is not { } pinned || pinned == candidate.Port)
        && request.Transport switch
        {
            MqttTransportMode.Tcp       => candidate.Transport == MqttTransport.Tcp,
            MqttTransportMode.WebSocket => candidate.Transport == MqttTransport.WebSocket,
            _ => true,
        }
        && request.Encryption switch
        {
            MqttEncryptionMode.On  => candidate.Encrypted,
            // Pinned off, except where the port itself fixes the scheme. A WebSocket front door on
            // 443 is encrypted by its address, which this setting cannot undo, and excluding it
            // would leave that port unreachable for everyone whose switch reads "off".
            MqttEncryptionMode.Off => !candidate.Encrypted
                || MqttEndpoint.Encrypts(candidate.Transport, candidate.Port, requested: false),
            _ => true,
        };

    private static bool Attempted(
        IReadOnlyList<MqttEndpointAttempt> attempts, MqttEndpointCandidate candidate)
    {
        foreach (var attempt in attempts)
            if (attempt.Candidate == candidate) return true;
        return false;
    }

    // Host names are case-insensitive and users paste them with stray spaces; a username is compared
    // the same way so a remembered entry cannot be missed over typing.
    private static bool Same(string a, string b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
