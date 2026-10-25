using ZeroZero.Primitives;

namespace ZeroZero.Mqtt;

/// <summary>What the log was last told about why there is no link, so a retry that finds the same
/// thing says nothing. One per <see cref="MqttConnection"/>; cleared on every connect.</summary>
internal sealed class MqttOutageLog(ILogSink log)
{
    private const string OutageReported = "outage";

    private string? _reported;

    // Set just before the connection ends its own session; taken by the disconnect, cleared on connect.
    private int _ownDrop;

    /// <summary>Publishing switched back on starts a fresh account.</summary>
    public void Forget() => Volatile.Write(ref _reported, null);

    /// <summary>A new session: nothing reported holds, and an unraised own drop belonged to the last.</summary>
    public void Connected()
    {
        Volatile.Write(ref _reported, null);
        Volatile.Write(ref _ownDrop, 0);
    }

    /// <summary>The disconnect about to be raised is the connection's own, not a lost link.</summary>
    public void OwnDrop() => Volatile.Write(ref _ownDrop, 1);

    /// <summary>Whether a lost link is news: once per outage, never for a session ended on purpose.</summary>
    public bool IsNewLoss() =>
        Interlocked.Exchange(ref _ownDrop, 0) == 0
        && Interlocked.Exchange(ref _reported, OutageReported) != OutageReported;

    /// <summary>Tells the log what a failed round found, once rather than on every retry. A refusal is
    /// an error, because retrying cannot change it; anything else is one line per outage.</summary>
    public void ReportFailedRound(IReadOnlyList<MqttEndpointAttempt> attempts)
    {
        var refused = attempts.Where(a => RefusalText(a.Outcome) is not null).ToList();
        if (refused.Count > 0)
        {
            string reason = string.Join("; ", refused.Select(a =>
                $"{Where(a)}: {RefusalText(a.Outcome)} ({a.Result.Detail})"));
            if (Interlocked.Exchange(ref _reported, reason) != reason)
                log.Error($"MqttConnection.Connect: the connection was refused — {reason}", null);
            return;
        }

        if (Interlocked.Exchange(ref _reported, OutageReported) != OutageReported)
            log.Info("MQTT: the server could not be reached — " +
                string.Join("; ", attempts.Select(a => $"{Where(a)}: {a.Result.Detail}")) +
                ". Sending resumes when the connection returns.");
    }

    // What waiting cannot fix. Null for every outcome that is only the link being down.
    private static string? RefusalText(MqttProbeOutcome outcome) => outcome switch
    {
        MqttProbeOutcome.AuthRejected => "the server refused the login",
        MqttProbeOutcome.Rejected => "the server refused the connection",
        MqttProbeOutcome.TlsUntrusted => "the server's certificate is not trusted",
        _ => null,
    };

    private static string Where(MqttEndpointAttempt attempt) =>
        $"{MqttStatusText.Name(attempt.Candidate.Transport)}:{attempt.Candidate.Port}";
}
