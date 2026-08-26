namespace ZeroZero.Mqtt;

/// <summary>One endpoint to try against the broker host: a port, a transport, and whether the link
/// is encrypted. The unit the endpoint plan deals in — no part is meaningful alone, because which
/// ports a broker answers on depends on which transport is being spoken, and whether a port is
/// served in cipher is the third thing a broker decides for itself. <see cref="Encrypted"/> is what
/// the link will actually be, not what was asked for: on a WebSocket port whose scheme is fixed by
/// convention the two differ.</summary>
public readonly record struct MqttEndpointCandidate(int Port, MqttTransport Transport, bool Encrypted = false);

/// <summary>Where the broker answered last, and for which host and user. State, never a setting, and
/// never a password: it records what was found, not what was chosen.</summary>
/// <remarks>
/// <para>It is deliberately absent from <see cref="MqttSettings"/> and from
/// <see cref="MqttConnectParameters"/>. Persisting it inside the settings record makes a successful
/// connect a settings change, and a consumer that reconfigures on a settings change then reconnects
/// on the strength of its own success. It reaches a host through
/// <see cref="MqttConnectionSetup.RememberEndpoint"/> instead, which the host is free to persist
/// wherever it keeps state.</para>
/// <para><see cref="Encrypted"/> is nullable because an entry written before it existed does not
/// say. Null is "not recorded", which is not the same as plain, and reading it as plain would leave
/// Automatic permanently satisfied with clear text on the strength of a default. An entry without it
/// is re-probed once under Automatic and rewritten complete; under an explicit choice it still
/// applies, because nothing is being asked about encryption there.</para>
/// </remarks>
public sealed record MqttEndpointMemory(
    string Host, string Username, int Port, MqttTransport Transport, bool? Encrypted = null);

/// <summary>The staged broker choices the plan reads. A null <see cref="Port"/> means "find it";
/// an explicit one pins every candidate to that port, exactly as an explicit transport pins the
/// transport and an explicit <see cref="Encryption"/> pins the scheme. Carries no password —
/// nothing the plan decides depends on one.</summary>
public readonly record struct MqttEndpointRequest(
    string Host, string Username, int? Port, MqttTransportMode Transport,
    MqttEncryptionMode Encryption = MqttEncryptionMode.Auto);

/// <summary>One finished endpoint attempt. Carries the whole result so the sentence afterwards can
/// name what each candidate did.</summary>
public readonly record struct MqttEndpointAttempt(
    MqttEndpointCandidate Candidate, MqttProbeResult Result)
{
    /// <summary>Outcome-only attempt, for callers with no detail to carry.</summary>
    public MqttEndpointAttempt(MqttEndpointCandidate candidate, MqttProbeOutcome outcome)
        : this(candidate, new MqttProbeResult(outcome, "")) { }

    public MqttProbeOutcome Outcome => Result.Outcome;
}

/// <summary>What asked for a probe. A closed set, and every member is a button the user pressed:
/// opening a settings page, re-showing a section, editing a field and a timer are all absent on
/// purpose, so a probe can only follow a deliberate act. <see cref="MqttEndpointPlan.ShouldProbe"/>
/// lists the members one by one rather than accepting whatever arrives, so a member added here has
/// to be considered there before it can put the machine on the network.</summary>
public enum MqttProbeTrigger
{
    /// <summary>The Test connection button.</summary>
    TestConnection,

    /// <summary>The Apply button, which is also what makes the staged values live.</summary>
    Apply,
}

/// <summary>Which stage of one candidate the sweep is on. The first two are worth telling apart
/// because they fail for different reasons: nothing listening on the port, versus something
/// listening that does not speak MQTT over this transport. <see cref="Finished"/> is the candidate's
/// own verdict, which is what turns a progress line into an account of the search rather than a
/// spinner with a port number on it.</summary>
public enum MqttSearchStage { Port, Transport, Finished }

/// <summary>What the sweep is doing right now, so a panel can say so. Pure data.
/// <see cref="Result"/> is carried by <see cref="MqttSearchStage.Finished"/> alone; the two
/// stages before it have nothing to report yet.</summary>
public readonly record struct MqttSearchProgress(
    MqttSearchStage Stage, int Port, MqttTransport Transport, MqttProbeResult? Result = null);
