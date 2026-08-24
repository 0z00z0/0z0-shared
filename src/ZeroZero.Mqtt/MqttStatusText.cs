using System.Globalization;

namespace ZeroZero.Mqtt;

/// <summary>Pure rendering of the status lines a settings panel shows. Callers pass the current
/// instant in rather than the formatter reading a clock, so the wording is testable without one, and
/// every probe sentence is composed here rather than beside the socket that produced it.</summary>
public static class MqttStatusText
{
    /// <summary>"just now" / "5 min ago" / "3 hours ago" / "2 days ago", or <paramref name="never"/>
    /// when there is nothing to render. A future timestamp reads as "just now", not a negative age.</summary>
    public static string Relative(DateTimeOffset? when, DateTimeOffset now, string never)
    {
        if (when is not { } stamp) return never;

        var age = now - stamp;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        // "min" is the abbreviation, so it does not pluralise.
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return Plural((int)age.TotalHours, "hour");
        return Plural((int)age.TotalDays, "day");
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";

    /// <summary>The "Broker in use" line: the saved host with whatever port and transport are in
    /// force, whether pinned by hand or found by probing. Pure.</summary>
    /// <remarks>A remembered endpoint is only ever read for its own host and user name, so a machine
    /// that has moved reports "not connected yet" rather than the address it used somewhere else.</remarks>
    public static string DescribeBroker(MqttEndpointRequest request, MqttEndpointMemory? memory)
    {
        string host = (request.Host ?? "").Trim();
        if (host.Length == 0) return "Not set";

        var found = MqttEndpointPlan.Reusable(request, memory);
        int? port = request.Port ?? found?.Port;
        MqttTransport? transport = request.Transport switch
        {
            MqttTransportMode.Tcp       => MqttTransport.Tcp,
            MqttTransportMode.WebSocket => MqttTransport.WebSocket,
            _ => found?.Transport,
        };

        // Half an answer is worse than none: "host:— over —" reads as a broken value rather than as
        // a question that has not been answered yet.
        if (port is not { } p || transport is not { } t) return $"{host} — not connected yet";
        return $"{host}:{p.ToString(CultureInfo.InvariantCulture)} over {Name(t)}";
    }

    /// <summary>The "Last publish" line.</summary>
    public static string DescribeLastPublish(DateTimeOffset? last, DateTimeOffset now) =>
        Relative(last, now, never: "Nothing published yet");

    /// <summary>The "Last command received" line — which command, and how long ago.
    /// <paramref name="label"/> turns an entity id into the name a user would recognise; without one
    /// the entity id stands, which is what the wire actually carried.</summary>
    public static string DescribeLastCommand(
        MqttCommandRecord? last, DateTimeOffset now, Func<string, string>? label = null) =>
        last is { } c
            ? $"{label?.Invoke(c.EntityId) ?? c.EntityId} — {Relative(c.When, now, never: "")}"
            : "Nothing received yet";

    /// <summary>How a transport is named to the user.</summary>
    public static string Name(MqttTransport transport) =>
        transport == MqttTransport.Tcp ? "TCP" : "WebSocket";

    /// <summary>How a connection state is named to the user.</summary>
    public static string Name(MqttConnectionState state) => state switch
    {
        MqttConnectionState.Disabled   => "Not publishing",
        MqttConnectionState.Searching  => "Looking for the broker",
        MqttConnectionState.Connecting => "Connecting",
        MqttConnectionState.Connected  => "Connected",
        MqttConnectionState.Retrying   => "Reconnecting",
        _                              => "Not connected",
    };

    /// <summary>The sentence shown for one transport's result. Pure, so the tests pin the wording.
    /// Every branch names the transport: under Auto that is the answer the user came for.</summary>
    public static string Describe(MqttProbeResult result, MqttTransport transport) => result.Outcome switch
    {
        MqttProbeOutcome.Success      => $"Connected over {Name(transport)}. The broker accepted these settings.",
        MqttProbeOutcome.Unreachable  => $"Could not reach the broker over {Name(transport)} — {Detail(result)}.",
        MqttProbeOutcome.TimedOut     => $"The broker did not answer over {Name(transport)} within {(int)MqttProbe.Timeout.TotalSeconds} seconds.",
        MqttProbeOutcome.AuthRejected => $"The broker answered over {Name(transport)} but rejected these credentials ({Detail(result)}).",
        MqttProbeOutcome.Rejected     => $"The broker refused the connection over {Name(transport)} ({Detail(result)}).",
        MqttProbeOutcome.TlsUntrusted => $"The encrypted connection over {Name(transport)} was not established — {Detail(result)}. Check the certificate trust setting.",
        MqttProbeOutcome.TlsUnsupported => $"The broker does not accept encrypted connections over {Name(transport)} on that port — {Detail(result)}.",
        _                             => $"The connection over {Name(transport)} failed — {Detail(result)}.",
    };

    /// <summary>What the sweep is doing right now, as a panel shows it under the button that started
    /// it. Pure, so the tests pin the wording. Every line names the endpoint, because under Automatic
    /// which one is being tried is most of what the user came to watch; the two attempt stages read
    /// differently because they mean different things — a port that never opens, versus a port that
    /// opens onto something not speaking MQTT — and the third is that candidate's own verdict.</summary>
    public static string Describe(MqttSearchProgress progress)
    {
        string endpoint = $"{Name(progress.Transport)} on port {progress.Port}";
        return progress.Stage switch
        {
            MqttSearchStage.Port      => $"Trying {endpoint}…",
            MqttSearchStage.Transport => $"Trying {endpoint} — asking the broker…",
            // Never reported without a result, but a missing one must not read as a success.
            _ => progress.Result is { } r ? $"{endpoint} {Clause(r)}." : $"{endpoint} — no answer recorded.",
        };
    }

    /// <summary>The sentence for a whole run. Pure.</summary>
    /// <remarks>
    /// The shapes differ because the user's next move does. Reaching the broker makes that attempt
    /// the story and any earlier one mere context ("connected over WebSocket, TCP was refused" sends
    /// nobody to check their password). Reaching nothing at all is the opposite: no single attempt
    /// explains it, so what was tried is listed — de-duplicated, because a sweep of eight candidates
    /// mostly repeats the same clause and a wall of them says less than one of each.
    /// </remarks>
    public static string Describe(MqttProbeReport report)
    {
        if (report.Attempts.Count == 0) return "No broker host set.";

        var last = report.Attempts[^1];
        string verdict = Describe(last.Result, last.Candidate.Transport);
        if (report.Attempts.Count == 1) return verdict;

        if (MqttEndpointPlan.Answered(last.Outcome))
            return $"{verdict} {Fragment(Context(report, last))}.";

        return "Neither transport reached the broker. " +
               string.Join("; ", report.Attempts.Select(Fragment).Distinct()) + ".";
    }

    /// <summary>Whether a result should be shown in the error colour rather than as plain status.</summary>
    public static bool IsFailure(MqttProbeReport report) => !report.Succeeded;

    /// <summary>The attempt worth naming beside the verdict: the last one on the other transport,
    /// which is the fact the user came for. Falls back to the one immediately before when the whole
    /// run stayed on a single transport.</summary>
    private static MqttEndpointAttempt Context(MqttProbeReport report, MqttEndpointAttempt verdict)
    {
        for (int i = report.Attempts.Count - 2; i >= 0; i--)
            if (report.Attempts[i].Candidate.Transport != verdict.Candidate.Transport)
                return report.Attempts[i];
        return report.Attempts[^2];
    }

    /// <summary>One attempt as a clause, for the sentences that name more than one transport.</summary>
    private static string Fragment(MqttEndpointAttempt attempt) =>
        $"{Name(attempt.Candidate.Transport)} {Clause(attempt.Result)}";

    /// <summary>What one endpoint did, with nothing naming the endpoint: the caller supplies that,
    /// so a summary clause and a progress line say the same thing about the same result.</summary>
    private static string Clause(MqttProbeResult result) => result.Outcome switch
    {
        MqttProbeOutcome.Success      => "connected",
        MqttProbeOutcome.Unreachable  => $"could not be reached ({Detail(result)})",
        MqttProbeOutcome.TimedOut     => "did not answer",
        MqttProbeOutcome.AuthRejected => "rejected these credentials",
        MqttProbeOutcome.Rejected     => $"was refused ({Detail(result)})",
        MqttProbeOutcome.TlsUntrusted => $"refused the encrypted connection ({Detail(result)})",
        MqttProbeOutcome.TlsUnsupported => "does not accept encrypted connections there",
        _                             => $"failed ({Detail(result)})",
    };

    // An exception message usually ends in its own full stop; the sentences above supply one.
    private static string Detail(MqttProbeResult result) => result.Detail.TrimEnd('.', ' ');
}
