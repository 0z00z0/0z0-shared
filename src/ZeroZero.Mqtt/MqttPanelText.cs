using System.Globalization;
using Microsoft.Extensions.Localization;

namespace ZeroZero.Mqtt;

/// <summary>Every sentence a settings panel renders, composed from live state. Pure: the instant is
/// passed in rather than read from a clock, and no call here touches a socket, a store or a control.</summary>
/// <remarks><see cref="MqttStatusText"/> is the static facade over <see cref="Default"/>.</remarks>
public sealed class MqttPanelText
{
    /// <summary>The module's own texts, with no host localizer in front of them.</summary>
    public static MqttPanelText Default { get; } = new(host: null);

    private readonly IStringLocalizer _text;

    /// <param name="host">Asked first for every key; not found or blank falls back to the module's.</param>
    public MqttPanelText(IStringLocalizer? host) => _text = MqttTexts.With(host);

    /// <summary>The texts behind this composer, host first, for a label it does not assemble.</summary>
    public IStringLocalizer Strings => _text;

    // ------------------------------------------------------------------------------------------
    // Relative ages.
    // ------------------------------------------------------------------------------------------

    /// <summary>"just now" / "5 minutes ago" / "3 hours ago" / "2 days ago", or <paramref name="never"/>
    /// when there is nothing to render. A future timestamp reads as "just now", not a negative
    /// age.</summary>
    public string Relative(DateTimeOffset? when, DateTimeOffset now, string never)
    {
        if (when is not { } stamp) return never;

        var age = now - stamp;
        if (age < TimeSpan.FromMinutes(1)) return _text["AgeJustNow"].Value;
        if (age < TimeSpan.FromHours(1)) return Counted("AgeMinutes", (int)age.TotalMinutes);
        if (age < TimeSpan.FromDays(1)) return Counted("AgeHours", (int)age.TotalHours);
        return Counted("AgeDays", (int)age.TotalDays);
    }

    private string Counted(string key, int count) =>
        MqttTexts.Count(_text, key, new Dictionary<string, object?> { ["count"] = count });

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
        if (string.IsNullOrWhiteSpace(request.Host)) return _text["StatusNoHost"].Value;
        // A probe in flight outranks the settled answer: it is the one thing on this row that is
        // happening rather than standing.
        if (probing) return _text["StatusProbing"].Value;
        return state == MqttConnectionState.Connected ? Provenance(request, memory) : Name(state);
    }

    /// <summary>Where the endpoint in force came from, and whether the link is in clear text.</summary>
    /// <remarks>The encryption clause is not decoration: Automatic falls back to plain on its own, so
    /// a link can be downgraded with no user action and nothing else on the page would say so.</remarks>
    public string Provenance(MqttEndpointRequest request, MqttEndpointMemory? memory)
    {
        string source = _text[MqttEndpointPlan.Pinned(request) ? "ProvenanceManual" : "ProvenanceAutomatic"].Value;

        return MqttEndpointPlan.EncryptionInForce(request, memory) switch
        {
            true  => _text["ProvenanceEncrypted", source].Value,
            false => _text["ProvenanceNotEncrypted", source].Value,
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
        if (host.Length == 0) return _text["StatusBrokerNotSet"].Value;

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
        if (port is not { } p || transport is not { } t) return _text["StatusBrokerUnsettled", host].Value;
        // The port is an address, not a quantity: an invariant rendering keeps a group separator out
        // of it whatever the display culture does with four digits.
        return _text["StatusBrokerInUse", host, p.ToString(CultureInfo.InvariantCulture), Name(t)].Value;
    }

    /// <summary>The "Last publish" row.</summary>
    public string DescribeLastPublish(DateTimeOffset? last, DateTimeOffset now) =>
        Relative(last, now, never: _text["StatusNothingPublished"].Value);

    /// <summary>The "Last command received" row — which command, and how long ago.
    /// <paramref name="label"/> turns an entity id into the name a user would recognise; without one
    /// the entity id stands, which is what the wire actually carried.</summary>
    public string DescribeLastCommand(
        MqttCommandRecord? last, DateTimeOffset now, Func<string, string>? label = null) =>
        last is { } c
            ? _text["StatusLastCommand", label?.Invoke(c.EntityId) ?? c.EntityId,
                           Relative(c.When, now, never: "")].Value
            : _text["StatusNothingReceived"].Value;

    // ------------------------------------------------------------------------------------------
    // Collapsed-section summaries.
    // ------------------------------------------------------------------------------------------

    /// <summary>The Broker section's line while it is closed: the host, and the port, transport and
    /// encryption it is configured with. What a section says about itself when it cannot be read.</summary>
    /// <remarks>
    /// <para>The instruction, not the outcome — the same split the Broker dropdowns and the Status
    /// rows already keep. A field set by hand shows its value plainly. A field left on Automatic
    /// shows the value in force marked as detected, so a summary never presents a swept result as a
    /// choice somebody made.</para>
    /// <para>A detected value is only shown while the link that found it is up. Once it is down the
    /// same value is a reading from an earlier moment, and printing it unqualified would be the very
    /// error the marking exists to prevent, reached by another route; the field falls back to the
    /// bare instruction instead.</para>
    /// <para><paramref name="trust"/> only ever adds the one clause that says the link accepts
    /// whatever certificate the broker presents — the platform's own trust leaves the line exactly as
    /// it reads without this parameter, because it still proves which machine answered and a summary
    /// that grew for it would be crying wolf.</para>
    /// </remarks>
    public MqttBrokerSummary SummariseBroker(
        MqttEndpointRequest request, MqttEndpointMemory? memory, MqttConnectionState state,
        MqttCertificateTrustMode trust)
    {
        string host = (request.Host ?? "").Trim().Replace(MqttBrokerSummary.Mark.ToString(), "");
        if (host.Length == 0) return new(_text["SummaryBrokerNotSet"].Value, []);

        var found = state == MqttConnectionState.Connected
            ? MqttEndpointPlan.Reusable(request, memory)
            : null;

        // The port the line is about: the pinned one, or the detected one while the link that found
        // it is up. The scheme a WebSocket address fixes is read from this rather than from the
        // setting alone, so a detected port settles it exactly as a pinned one does.
        int? inForce = request.Port ?? found?.Port;

        // The port is an address, not a quantity: an invariant rendering keeps a group separator out
        // of it whatever the display culture does with four digits.
        string port = request.Port is { } pinned
            ? pinned.ToString(CultureInfo.InvariantCulture)
            : Detected(found?.Port.ToString(CultureInfo.InvariantCulture));

        string transport = request.Transport switch
        {
            MqttTransportMode.Tcp       => Name(MqttTransport.Tcp),
            MqttTransportMode.WebSocket => Name(MqttTransport.WebSocket),
            _ => Detected(found is { } f ? Name(f.Transport) : null),
        };

        string encryption = request.Encryption switch
        {
            // An explicit choice is the instruction, except where the address fixes the scheme
            // whatever the setting says — a WebSocket front door on 443 is encrypted by its port,
            // and a summary denying that would put a false statement about the wire beside a Status
            // row asserting the opposite. Only the port reaches this from what was found; the rest
            // of the answer is the settings' own, and nothing here is detected to mark.
            MqttEncryptionMode.On or MqttEncryptionMode.Off =>
                Encryption(MqttEndpointPlan.EncryptionInForce(
                    request with { Port = inForce }, memory: null) is true),
            _ => Detected(found?.Encrypted is { } encrypted ? Encryption(encrypted) : null),
        };

        string summary = _text["SummaryBroker", host, port, transport, encryption].Value;

        // The one mode that gives up proving which machine answered, so it is the one mode this line
        // says anything about. Wrapping the finished line rather than adding a fifth argument keeps
        // the platform's own trust byte-for-byte what it reads without it.
        return MqttBrokerSummary.FromMarked(trust == MqttCertificateTrustMode.AcceptAny
            ? _text["SummaryBrokerTrustAny", summary].Value
            : summary);
    }

    /// <summary>The publish section's line while it is closed: how many declared groups are switched
    /// on.</summary>
    /// <remarks>A count rather than the group names, because the module declares no group of its own
    /// and a consumer may declare any number — a list grows without bound where a count stays one
    /// short true fact. The entities behind the groups are never counted here: the panel is not shown
    /// the entity set, so a number for it could only be invented.</remarks>
    public string SummarisePublish(MqttPublishTally tally) =>
        tally.Declared == 0
            ? _text["SummaryPublishNoGroups"].Value
            : MqttTexts.Count(_text, "SummaryPublish",
                new Dictionary<string, object?> { ["on"] = tally.SwitchedOn, ["total"] = tally.Declared });

    /// <summary>A value that was found rather than chosen, marked as such — or the bare instruction
    /// when nothing has found one yet. The mark travels with the value, so a translation may reorder
    /// the line freely.</summary>
    private string Detected(string? value) => value is { Length: > 0 } found
        ? found + MqttBrokerSummary.Mark
        : _text["OptionAutomatic"].Value;

    private string Encryption(bool on) =>
        _text[on ? "SummaryEncrypted" : "SummaryNotEncrypted"].Value;

    // ------------------------------------------------------------------------------------------
    // Vocabulary.
    // ------------------------------------------------------------------------------------------

    /// <summary>How a transport is named to the user.</summary>
    public string Name(MqttTransport transport) =>
        _text[transport == MqttTransport.Tcp ? "TransportTcp" : "TransportWebSocket"].Value;

    /// <summary>How a connection state is named to the user.</summary>
    public string Name(MqttConnectionState state) => _text[state switch
    {
        MqttConnectionState.Disabled   => "StateDisabled",
        MqttConnectionState.Searching  => "StateSearching",
        MqttConnectionState.Connecting => "StateConnecting",
        MqttConnectionState.Connected  => "StateConnected",
        MqttConnectionState.Retrying   => "StateRetrying",
        _                              => "StateNotConnected",
    }].Value;

    // ------------------------------------------------------------------------------------------
    // The probe: one candidate, the sweep as it happens, and the whole run.
    // ------------------------------------------------------------------------------------------

    /// <summary>The sentence shown for one transport's result. Every branch names the transport:
    /// under Automatic that is the answer the user came for.</summary>
    public string Describe(MqttProbeResult result, MqttTransport transport) => result.Outcome switch
    {
        MqttProbeOutcome.Success        => _text["ProbeSuccess", Name(transport)].Value,
        MqttProbeOutcome.Unreachable    => _text["ProbeUnreachable", Name(transport), Detail(result)].Value,
        MqttProbeOutcome.TimedOut       => _text["ProbeTimedOut", Name(transport), (int)MqttProbe.Timeout.TotalSeconds].Value,
        MqttProbeOutcome.AuthRejected   => _text["ProbeAuthRejected", Name(transport), Detail(result)].Value,
        MqttProbeOutcome.Rejected       => _text["ProbeRejected", Name(transport), Detail(result)].Value,
        MqttProbeOutcome.TlsUntrusted   => _text["ProbeTlsUntrusted", Name(transport), Detail(result)].Value,
        MqttProbeOutcome.TlsUnsupported => _text["ProbeTlsUnsupported", Name(transport), Detail(result)].Value,
        _                               => _text["ProbeFailed", Name(transport), Detail(result)].Value,
    };

    /// <summary>What the sweep is doing right now, as a panel shows it under the button that started
    /// it. Every line names the endpoint, because under Automatic which one is being tried is most of
    /// what the user came to watch; the two attempt stages read differently because they mean
    /// different things — a port that never opens, versus a port that opens onto something not
    /// speaking MQTT — and the third is that candidate's own verdict.</summary>
    public string Describe(MqttSearchProgress progress)
    {
        string endpoint = _text["ProgressEndpoint", Name(progress.Transport),
                                       progress.Port.ToString(CultureInfo.InvariantCulture)].Value;
        return progress.Stage switch
        {
            MqttSearchStage.Port      => _text["ProgressPort", endpoint].Value,
            MqttSearchStage.Transport => _text["ProgressTransport", endpoint].Value,
            // Never reported without a result, but a missing one must not read as a success.
            _ => progress.Result is { } r
                ? _text["ProgressFinished", endpoint, Clause(r)].Value
                : _text["ProgressNoAnswer", endpoint].Value,
        };
    }

    /// <summary>The sentence for a whole run: the outcome, never the attempts it took to get
    /// there.</summary>
    /// <remarks>
    /// <para>The question the button was pressed to answer is whether these settings work, so a run
    /// that connected is answered by the endpoint it connected on and by nothing else. An attempt
    /// that failed on the way there is the search describing itself, and printed under a success it
    /// reads as a fault to go and investigate.</para>
    /// <para>A broker that answered and said no is its own whole answer for the same reason: what
    /// else was tried adds nothing to a rejected credential. Only a run that reached nothing at all
    /// lists what it tried, because there the attempts are the finding — de-duplicated, since a
    /// sweep of eight candidates mostly repeats one clause and a wall of them says less than one of
    /// each.</para>
    /// </remarks>
    public string Describe(MqttProbeReport report)
    {
        if (report.Attempts.Count == 0) return _text["ReportNoHost"].Value;

        var last = report.Attempts[^1];

        // The working configuration, which is the other half of "does this work": the port is an
        // address, so an invariant rendering keeps a group separator out of four digits.
        if (report.Succeeded)
            return _text["ReportConnected", Name(last.Candidate.Transport),
                                last.Candidate.Port.ToString(CultureInfo.InvariantCulture)].Value;

        if (report.Attempts.Count == 1 || MqttEndpointPlan.Answered(last.Outcome))
            return Describe(last.Result, last.Candidate.Transport);

        return _text["ReportNothingReached",
            string.Join(_text["ReportFragmentJoin"].Value,
                        report.Attempts.Select(Fragment).Distinct(StringComparer.Ordinal))].Value;
    }

    /// <summary>Whether a result should be shown in the error colour rather than as plain status.</summary>
    public static bool IsFailure(MqttProbeReport report) => !report.Succeeded;

    /// <summary>One attempt as a clause, for the sentences that name more than one transport.</summary>
    private string Fragment(MqttEndpointAttempt attempt) =>
        _text["ReportFragment", Name(attempt.Candidate.Transport), Clause(attempt.Result)].Value;

    /// <summary>What one endpoint did, with nothing naming the endpoint: the caller supplies that,
    /// so a summary clause and a progress line say the same thing about the same result.</summary>
    private string Clause(MqttProbeResult result) => result.Outcome switch
    {
        MqttProbeOutcome.Success        => _text["ClauseSuccess"].Value,
        MqttProbeOutcome.Unreachable    => _text["ClauseUnreachable", Detail(result)].Value,
        MqttProbeOutcome.TimedOut       => _text["ClauseTimedOut"].Value,
        MqttProbeOutcome.AuthRejected   => _text["ClauseAuthRejected"].Value,
        MqttProbeOutcome.Rejected       => _text["ClauseRejected", Detail(result)].Value,
        MqttProbeOutcome.TlsUntrusted   => _text["ClauseTlsUntrusted", Detail(result)].Value,
        MqttProbeOutcome.TlsUnsupported => _text["ClauseTlsUnsupported"].Value,
        _                               => _text["ClauseFailed", Detail(result)].Value,
    };

    // An exception message usually ends in its own full stop; the sentences above supply one.
    private static string Detail(MqttProbeResult result) => result.Detail.TrimEnd('.', ' ');
}
