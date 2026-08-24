using System.Net.Sockets;

namespace ZeroZero.Mqtt;

/// <summary>The three the user needs to tell apart are <see cref="Unreachable"/>,
/// <see cref="AuthRejected"/> and <see cref="Success"/>; the rest keep those from widening into
/// catch-alls.</summary>
public enum MqttProbeOutcome
{
    /// <summary>CONNACK accepted the session.</summary>
    Success,

    /// <summary>DNS or TCP never got us a broker: unknown host, refused, no route.</summary>
    Unreachable,

    /// <summary>Something is at that address but it never answered within the budget.</summary>
    TimedOut,

    /// <summary>CONNACK: bad username or password, or not authorised.</summary>
    AuthRejected,

    /// <summary>CONNACK: refused for some other reason — client id, protocol, banned.</summary>
    Rejected,

    /// <summary>A socket opened, the far end presented a certificate, and the link still failed: the
    /// certificate is not trusted, or the protocols do not meet. Encryption <b>was</b> on offer, so a
    /// clear-text retry would send the password to a broker that could have taken it in cipher — this
    /// is the outcome that must never be downgraded, and certificate trust is what resolves it.</summary>
    TlsUntrusted,

    /// <summary>A socket opened and the far end never presented a certificate: it does not speak TLS
    /// on this port. Nothing secure was ever on offer and no credentials left the machine — the
    /// handshake fails before CONNECT — so a clear-text retry on the same endpoint is what Automatic
    /// means, and is the ordinary way an internal broker on 1883 is found.</summary>
    TlsUnsupported,

    /// <summary>Protocol error, or anything else.</summary>
    Failed,
}

/// <summary><see cref="MqttProbeOutcome"/> plus a short broker or OS-supplied reason. Never carries
/// credentials.</summary>
public readonly record struct MqttProbeResult(MqttProbeOutcome Outcome, string Detail);

/// <summary>The staged broker values a probe should try. Mirrors the fields a settings panel stages,
/// plus the transport setting and the remembered endpoint — the probe walks the same plan the live
/// connection does, so the button's verdict is about the connection that will actually be made.
/// A null <see cref="Port"/> means the port is to be found rather than assumed.</summary>
/// <remarks>Certificate trust is carried here and not on <see cref="MqttEndpointRequest"/>: nothing
/// the plan decides depends on it, but everything the handshake does. A probe run under different
/// trust from the connection would pass where the connection fails.</remarks>
public readonly record struct MqttProbeTarget(
    string Host, int? Port, string Username, string Password, string ClientId,
    MqttTransportMode Transport = MqttTransportMode.Auto,
    MqttEncryptionMode Encryption = MqttEncryptionMode.Auto,
    MqttEndpointMemory? Memory = null,
    MqttCertificateTrust? CertificateTrust = null)
{
    /// <summary>The staged choices as the pure plan reads them. The password never crosses this
    /// line: nothing the plan decides depends on it.</summary>
    public MqttEndpointRequest Request => new(Host, Username, Port, Transport, Encryption);

    /// <summary>The trust in force, defaulting to the platform's own stores.</summary>
    public MqttCertificateTrust Trust => CertificateTrust ?? MqttCertificateTrust.SystemTrust;
}

/// <summary>Every endpoint the probe got as far as trying, in order. The last attempt is the
/// verdict; the earlier ones are why it came to that.</summary>
public readonly record struct MqttProbeReport(IReadOnlyList<MqttEndpointAttempt> Attempts)
{
    /// <summary>Empty only when there was nothing to try, so <see cref="MqttProbeOutcome.Failed"/>
    /// stands in rather than a fake success.</summary>
    public MqttProbeOutcome Outcome =>
        Attempts.Count == 0 ? MqttProbeOutcome.Failed : Attempts[^1].Outcome;

    /// <summary>The port and transport the verdict came from — what is remembered on success.</summary>
    public MqttEndpointCandidate Candidate =>
        Attempts.Count == 0 ? new(0, MqttTransport.Tcp) : Attempts[^1].Candidate;

    /// <summary>The transport the verdict came from.</summary>
    public MqttTransport Transport => Candidate.Transport;

    public bool Succeeded => Outcome == MqttProbeOutcome.Success;
}

/// <summary>A throwaway CONNECT against the broker, behind a "Test connection" action.</summary>
/// <remarks>
/// Its own short-lived client, never the live <see cref="MqttConnection"/>: a broker kicks off any
/// existing session holding the same client id, so reusing the device id would drop the live
/// connection on every button press. The probe publishes nothing and sets no Last Will. A plain TCP
/// connect comes first, because that is what makes "unreachable" a precise verdict straight from the
/// OS — a client-library exception reads the same for a typo'd host and a wrong password. Only once a
/// socket opens is CONNECT sent, where a rejection can only be credentials or session. Both stages
/// run per candidate, so a sweep reports which port and transport answered rather than only that
/// something did. It runs on user initiation alone — see <see cref="MqttEndpointPlan.ShouldProbe"/>.
/// </remarks>
public static class MqttProbe
{
    /// <summary>
    /// Both stages together, for a run with one candidate to try. 10 s matches a desktop
    /// application's other network timeouts and beats Windows' own ~21 s SYN-retry give-up, so an
    /// unreachable IP reports while the user is still looking.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>What each candidate gets when several are being swept. A filtered port costs the
    /// whole budget, and the sweep can hold eight candidates, so the full 10 s would put the answer
    /// over a minute away. 4 s still clears a LAN round trip and a TLS handshake by a wide margin.</summary>
    public static readonly TimeSpan SweepTimeout = TimeSpan.FromSeconds(4);

    /// <summary>The probe's client id — never the publisher's, see the class note.</summary>
    public static string ProbeClientId(string deviceId) => $"{deviceId}_probe";

    /// <summary>Never throws: every failure comes back as an outcome. <paramref name="ct"/> is the
    /// caller's cancellation (window closing, or the host being retyped), with the per-candidate
    /// budget applied on top. <paramref name="progress"/> is reported on the probing thread, so a
    /// UI caller marshals it itself.</summary>
    public static async Task<MqttProbeReport> RunAsync(
        MqttProbeTarget target, CancellationToken ct,
        IProgress<MqttSearchProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(target.Host)) return new([]);

        var request = target.Request;
        var budgetPerCandidate =
            MqttEndpointPlan.Sweep(request, target.Memory).Count > 1 ? SweepTimeout : Timeout;

        var attempts = new List<MqttEndpointAttempt>();
        while (MqttEndpointPlan.NextEndpoint(request, target.Memory, attempts) is { } candidate)
        {
            // A fresh budget per candidate: one dead endpoint must not eat the next one's chance.
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(budgetPerCandidate);
            var result = await RunOneAsync(target, candidate, progress, budget.Token, ct).ConfigureAwait(false);
            attempts.Add(new(candidate, result));

            if (ct.IsCancellationRequested) break;   // cancelled; the remaining candidates are moot
        }
        return new(attempts);
    }

    /// <summary>Both stages against one candidate. The two stages are reported separately because
    /// they fail for different reasons, and a panel says which one is running.</summary>
    private static async Task<MqttProbeResult> RunOneAsync(
        MqttProbeTarget target, MqttEndpointCandidate candidate,
        IProgress<MqttSearchProgress>? progress, CancellationToken budget, CancellationToken ct)
    {
        var address = MqttEndpoint.Resolve(target.Host, candidate);

        progress?.Report(new(MqttSearchStage.Port, address.Port, candidate.Transport));
        if (await ProbeTcpAsync(address.Host, address.Port, budget, ct).ConfigureAwait(false) is { } closed)
        {
            progress?.Report(new(MqttSearchStage.Finished, address.Port, candidate.Transport, closed));
            return closed;
        }

        progress?.Report(new(MqttSearchStage.Transport, address.Port, candidate.Transport));
        var result = await MqttClientWiring
            .ProbeConnectAsync(target, address, budget, ct).ConfigureAwait(false);
        progress?.Report(new(MqttSearchStage.Finished, address.Port, candidate.Transport, result));
        return result;
    }

    /// <summary>Stage 1 — can a socket be opened at all. Returns null when it can (i.e. carry on).</summary>
    internal static async Task<MqttProbeResult?> ProbeTcpAsync(
        string host, int port, CancellationToken budget, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, budget).ConfigureAwait(false);
            return null;
        }
        catch (SocketException ex) { return ClassifySocketError(ex.SocketErrorCode); }
        catch (OperationCanceledException) { return Cancelled(ct); }
        catch (Exception ex) { return new(MqttProbeOutcome.Failed, Describe(ex)); }
    }

    /// <summary>Maps a CONNACK reason code to an outcome. Pure.</summary>
    public static MqttProbeResult ClassifyConnack(MqttConnackCode code, string? reason) => code switch
    {
        MqttConnackCode.Success => new(MqttProbeOutcome.Success, ""),

        // A broker with anonymous access disabled answers "not authorised" to a blank username, which
        // is the same user error as a wrong password.
        MqttConnackCode.BadUserNameOrPassword or MqttConnackCode.NotAuthorised =>
            new(MqttProbeOutcome.AuthRejected, Reason(code, reason)),

        // Everything else the broker can say no with — a real answer, so never "unreachable".
        _ => new(MqttProbeOutcome.Rejected, Reason(code, reason)),
    };

    /// <summary>Maps an OS socket error to an outcome. Pure.</summary>
    public static MqttProbeResult ClassifySocketError(SocketError error) => error switch
    {
        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain =>
            new(MqttProbeOutcome.Unreachable, "host name could not be resolved"),
        SocketError.ConnectionRefused =>
            new(MqttProbeOutcome.Unreachable, "nothing is listening on that port"),
        SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
            new(MqttProbeOutcome.Unreachable, "no route to that host"),
        SocketError.TimedOut =>
            new(MqttProbeOutcome.TimedOut, "no answer"),
        _ => new(MqttProbeOutcome.Failed, error.ToString()),
    };

    /// <summary>Classifies a failed connect attempt from what the exception chain carries.</summary>
    /// <param name="certificatePresented">Whether the far end presented a certificate during this
    /// attempt, or null when the attempt was not an encrypted one and the question does not arise.
    /// It is what separates the two TLS failures, and the separation cannot be made from the
    /// exception: the same close, reset or stall carries both, and the wording that would tell them
    /// apart is the platform's and is translated.</param>
    /// <remarks>
    /// On an encrypted attempt the certificate is the whole verdict and the exception type carries
    /// no weight. A broker that does not speak TLS on its port reads a ClientHello as a malformed
    /// packet and closes the socket, and that arrives as a client-library communication exception
    /// wrapping an end of stream — no <see cref="SocketException"/> and no authentication failure
    /// anywhere in the chain. A reset, an abort and a stalled handshake reach here in as many other
    /// shapes. Deciding on any of those types would leave the ordinary internal broker on 1883
    /// unreachable under Automatic, which is what the witness exists to prevent.
    /// </remarks>
    public static MqttProbeResult ClassifyConnectException(
        Exception ex, CancellationToken ct, bool? certificatePresented = null)
    {
        SocketException? socket = null;
        bool cancelled = false;
        bool handshake = false;

        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            cancelled |= e is OperationCanceledException;
            handshake |= e is System.Security.Authentication.AuthenticationException;
            socket ??= e as SocketException;
        }

        // Ahead of the witness deliberately: an expired budget cannot say whether a handshake ever
        // began, so it keeps its own verdict rather than being read as an absent certificate.
        if (cancelled) return Cancelled(ct);

        // The OS saying there is nothing there, or nothing answering, stands on its own: it is about
        // the address, not about what the address speaks. These are also the failures of an attempt
        // whose handshake never started, so the witness has nothing to say about them and must not
        // turn a filtered port into "no TLS there".
        if (socket is not null
            && ClassifySocketError(socket.SocketErrorCode) is
               { Outcome: MqttProbeOutcome.Unreachable or MqttProbeOutcome.TimedOut } reached)
            return reached;

        // What is left of an encrypted attempt is a handshake that began and did not finish, and the
        // certificate settles it: one that arrived means encryption was on offer and something is
        // wrong with it; none means nothing secure was ever offered and no credential left the
        // machine, so the plain retry behind this candidate is safe.
        if (certificatePresented is { } presented)
            return new(
                presented ? MqttProbeOutcome.TlsUntrusted : MqttProbeOutcome.TlsUnsupported,
                Describe(ex));

        if (handshake) return new(MqttProbeOutcome.TlsUntrusted, Describe(ex));
        if (socket is not null) return ClassifySocketError(socket.SocketErrorCode);
        return new(MqttProbeOutcome.Failed, Describe(ex));
    }

    // A cancelled budget with the caller's token still live means the timeout fired, not the user.
    internal static MqttProbeResult Cancelled(CancellationToken ct) => ct.IsCancellationRequested
        ? new(MqttProbeOutcome.Failed, "cancelled")
        : new(MqttProbeOutcome.TimedOut, "no answer");

    private static string Reason(MqttConnackCode code, string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? code.ToString() : $"{code}: {reason.Trim()}";

    // Type and message only, mirroring MqttConnection's sanitiser: both are broker or OS-generated,
    // so no staged credential can ride out of here into the UI.
    internal static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
