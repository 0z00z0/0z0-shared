using System.Globalization;

namespace ZeroZero.Mqtt;

/// <summary>Every sentence a settings panel renders, composed from live state through
/// <see cref="MqttStrings"/>. Pure: the instant is passed in rather than read from a clock, and no
/// call here touches a socket, a store or a control.</summary>
/// <remarks>
/// <para>An instance rather than a static so the string source is a constructor argument. A panel
/// hands it one backed by the host's resource map; a test hands it none and pins the module's own
/// en-GB, which is the same text either way when nothing is translated.</para>
/// <para><see cref="MqttStatusText"/> is the static facade over <see cref="Default"/>, kept because
/// most callers want the module's own wording and nothing else.</para>
/// </remarks>
public sealed class MqttPanelText
{
    /// <summary>The module's own en-GB.</summary>
    public static MqttPanelText Default { get; } = new(MqttStrings.Default);

    private readonly MqttStrings _text;

    public MqttPanelText(MqttStrings text) => _text = text;

    public MqttPanelText(IMqttStringSource? source) : this(new MqttStrings(source)) { }

    /// <summary>The strings behind this composer, for a caller that needs one the composer does not
    /// itself assemble — a button label, a heading, a validation message.</summary>
    public MqttStrings Strings => _text;

    // ------------------------------------------------------------------------------------------
    // Relative ages.
    // ------------------------------------------------------------------------------------------

    /// <summary>"just now" / "5 min ago" / "3 hours ago" / "2 days ago", or <paramref name="never"/>
    /// when there is nothing to render. A future timestamp reads as "just now", not a negative
    /// age.</summary>
    public string Relative(DateTimeOffset? when, DateTimeOffset now, string never)
    {
        if (when is not { } stamp) return never;

        var age = now - stamp;
        if (age < TimeSpan.FromMinutes(1)) return _text.Get("AgeJustNow");
        if (age < TimeSpan.FromHours(1)) return _text.Format("AgeMinutes", (int)age.TotalMinutes);
        if (age < TimeSpan.FromDays(1)) return Plural((int)age.TotalHours, "AgeHour", "AgeHours");
        return Plural((int)age.TotalDays, "AgeDay", "AgeDays");
    }

    // Two forms, chosen in code. English needs exactly these two and .NET supplies neither, so the
    // choice is a branch rather than a rule engine.
    private string Plural(int n, string singular, string plural) =>
        _text.Format(n == 1 ? singular : plural, n);

    // ------------------------------------------------------------------------------------------
    // The four Status values.
    // ------------------------------------------------------------------------------------------

    /// <summary>The Connection row: how the endpoint in force was arrived at, or what the connection
    /// is doing while nothing has settled.</summary>
    /// <remarks>Deliberately not the same fact as the Broker dropdowns, which carry the instruction,
    /// or as <see cref="DescribeBroker"/>, which carries the address it landed on. A sweep leaves all
    /// three saying different things and each is the answer to a different question.</remarks>
    public string Connection(
        MqttEndpointRequest request, MqttEndpointMemory? memory,
        MqttConnectionState state, bool probing)
    {
        if (string.IsNullOrWhiteSpace(request.Host)) return _text.Get("StatusNoHost");
        // A probe in flight outranks the settled answer: it is the one thing on this row that is
        // happening rather than standing.
        if (probing) return _text.Get("StatusProbing");
        return state == MqttConnectionState.Connected ? Provenance(request, memory) : Name(state);
    }

    /// <summary>Where the endpoint in force came from, and whether the link is in clear text.</summary>
    /// <remarks>The encryption clause is not decoration: Automatic falls back to plain on its own, so
    /// a link can be downgraded with no user action and nothing else on the page would say so.</remarks>
    public string Provenance(MqttEndpointRequest request, MqttEndpointMemory? memory)
    {
        string source = _text.Get(MqttEndpointPlan.Pinned(request) ? "ProvenanceManual" : "ProvenanceAutomatic");

        return MqttEndpointPlan.EncryptionInForce(request, memory) switch
        {
            true  => _text.Format("ProvenanceEncrypted", source),
            false => _text.Format("ProvenanceNotEncrypted", source),
            _     => source,
        };
    }

    /// <summary>The "Broker in use" row: the saved host with whatever port and transport are in
    /// force, whether pinned by hand or found by probing.</summary>
    /// <remarks>A remembered endpoint is only ever read for its own host and user name, so a machine
    /// that has moved reports "not connected yet" rather than the address it used somewhere else.</remarks>
    public string DescribeBroker(MqttEndpointRequest request, MqttEndpointMemory? memory)
    {
        string host = (request.Host ?? "").Trim();
        if (host.Length == 0) return _text.Get("StatusBrokerNotSet");

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
        if (port is not { } p || transport is not { } t) return _text.Format("StatusBrokerUnsettled", host);
        // The port is an address, not a quantity: an invariant rendering keeps a group separator out
        // of it whatever the display culture does with four digits.
        return _text.Format("StatusBrokerInUse", host, p.ToString(CultureInfo.InvariantCulture), Name(t));
    }

    /// <summary>The "Last publish" row.</summary>
    public string DescribeLastPublish(DateTimeOffset? last, DateTimeOffset now) =>
        Relative(last, now, never: _text.Get("StatusNothingPublished"));

    /// <summary>The "Last command received" row — which command, and how long ago.
    /// <paramref name="label"/> turns an entity id into the name a user would recognise; without one
    /// the entity id stands, which is what the wire actually carried.</summary>
    public string DescribeLastCommand(
        MqttCommandRecord? last, DateTimeOffset now, Func<string, string>? label = null) =>
        last is { } c
            ? _text.Format("StatusLastCommand", label?.Invoke(c.EntityId) ?? c.EntityId,
                           Relative(c.When, now, never: ""))
            : _text.Get("StatusNothingReceived");

    // ------------------------------------------------------------------------------------------
    // Vocabulary.
    // ------------------------------------------------------------------------------------------

    /// <summary>How a transport is named to the user.</summary>
    public string Name(MqttTransport transport) =>
        _text.Get(transport == MqttTransport.Tcp ? "TransportTcp" : "TransportWebSocket");

    /// <summary>How a connection state is named to the user.</summary>
    public string Name(MqttConnectionState state) => _text.Get(state switch
    {
        MqttConnectionState.Disabled   => "StateDisabled",
        MqttConnectionState.Searching  => "StateSearching",
        MqttConnectionState.Connecting => "StateConnecting",
        MqttConnectionState.Connected  => "StateConnected",
        MqttConnectionState.Retrying   => "StateRetrying",
        _                              => "StateNotConnected",
    });

    // ------------------------------------------------------------------------------------------
    // The probe: one candidate, the sweep as it happens, and the whole run.
    // ------------------------------------------------------------------------------------------

    /// <summary>The sentence shown for one transport's result. Every branch names the transport:
    /// under Automatic that is the answer the user came for.</summary>
    public string Describe(MqttProbeResult result, MqttTransport transport) => result.Outcome switch
    {
        MqttProbeOutcome.Success        => _text.Format("ProbeSuccess", Name(transport)),
        MqttProbeOutcome.Unreachable    => _text.Format("ProbeUnreachable", Name(transport), Detail(result)),
        MqttProbeOutcome.TimedOut       => _text.Format("ProbeTimedOut", Name(transport), (int)MqttProbe.Timeout.TotalSeconds),
        MqttProbeOutcome.AuthRejected   => _text.Format("ProbeAuthRejected", Name(transport), Detail(result)),
        MqttProbeOutcome.Rejected       => _text.Format("ProbeRejected", Name(transport), Detail(result)),
        MqttProbeOutcome.TlsUntrusted   => _text.Format("ProbeTlsUntrusted", Name(transport), Detail(result)),
        MqttProbeOutcome.TlsUnsupported => _text.Format("ProbeTlsUnsupported", Name(transport), Detail(result)),
        _                               => _text.Format("ProbeFailed", Name(transport), Detail(result)),
    };

    /// <summary>What the sweep is doing right now, as a panel shows it under the button that started
    /// it. Every line names the endpoint, because under Automatic which one is being tried is most of
    /// what the user came to watch; the two attempt stages read differently because they mean
    /// different things — a port that never opens, versus a port that opens onto something not
    /// speaking MQTT — and the third is that candidate's own verdict.</summary>
    public string Describe(MqttSearchProgress progress)
    {
        string endpoint = _text.Format("ProgressEndpoint", Name(progress.Transport),
                                       progress.Port.ToString(CultureInfo.InvariantCulture));
        return progress.Stage switch
        {
            MqttSearchStage.Port      => _text.Format("ProgressPort", endpoint),
            MqttSearchStage.Transport => _text.Format("ProgressTransport", endpoint),
            // Never reported without a result, but a missing one must not read as a success.
            _ => progress.Result is { } r
                ? _text.Format("ProgressFinished", endpoint, Clause(r))
                : _text.Format("ProgressNoAnswer", endpoint),
        };
    }

    /// <summary>The sentence for a whole run.</summary>
    /// <remarks>
    /// The shapes differ because the user's next move does. Reaching the broker makes that attempt
    /// the story and any earlier one mere context ("connected over WebSocket, TCP was refused" sends
    /// nobody to check their password). Reaching nothing at all is the opposite: no single attempt
    /// explains it, so what was tried is listed — de-duplicated, because a sweep of eight candidates
    /// mostly repeats the same clause and a wall of them says less than one of each.
    /// </remarks>
    public string Describe(MqttProbeReport report)
    {
        if (report.Attempts.Count == 0) return _text.Get("ReportNoHost");

        var last = report.Attempts[^1];
        string verdict = Describe(last.Result, last.Candidate.Transport);
        if (report.Attempts.Count == 1) return verdict;

        if (MqttEndpointPlan.Answered(last.Outcome))
            return _text.Format("ReportWithContext", verdict, Fragment(Context(report, last)));

        return _text.Format("ReportNothingReached",
            string.Join(_text.Get("ReportFragmentJoin"),
                        report.Attempts.Select(Fragment).Distinct(StringComparer.Ordinal)));
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
    private string Fragment(MqttEndpointAttempt attempt) =>
        _text.Format("ReportFragment", Name(attempt.Candidate.Transport), Clause(attempt.Result));

    /// <summary>What one endpoint did, with nothing naming the endpoint: the caller supplies that,
    /// so a summary clause and a progress line say the same thing about the same result.</summary>
    private string Clause(MqttProbeResult result) => result.Outcome switch
    {
        MqttProbeOutcome.Success        => _text.Get("ClauseSuccess"),
        MqttProbeOutcome.Unreachable    => _text.Format("ClauseUnreachable", Detail(result)),
        MqttProbeOutcome.TimedOut       => _text.Get("ClauseTimedOut"),
        MqttProbeOutcome.AuthRejected   => _text.Get("ClauseAuthRejected"),
        MqttProbeOutcome.Rejected       => _text.Format("ClauseRejected", Detail(result)),
        MqttProbeOutcome.TlsUntrusted   => _text.Format("ClauseTlsUntrusted", Detail(result)),
        MqttProbeOutcome.TlsUnsupported => _text.Get("ClauseTlsUnsupported"),
        _                               => _text.Format("ClauseFailed", Detail(result)),
    };

    // An exception message usually ends in its own full stop; the sentences above supply one.
    private static string Detail(MqttProbeResult result) => result.Detail.TrimEnd('.', ' ');
}
