namespace ZeroZero.Mqtt;

/// <summary>What became of one inbound message. Everything but <see cref="Accepted"/> publishes
/// nothing, changes nothing and clamps nothing — a refusal is a refusal, not a correction.</summary>
public enum MqttCommandOutcome
{
    Accepted,

    /// <summary>No declared command entity owns that topic.</summary>
    Unrecognised,

    /// <summary>The message arrived retained, so it is a replay rather than a request.</summary>
    Retained,

    /// <summary>The payload does not parse as the value this entity takes.</summary>
    Malformed,

    /// <summary>It parses, but falls outside the bounds the application enforces.</summary>
    OutOfRange,

    /// <summary>It parses, but is not one of the options the entity currently offers.</summary>
    NotAnOption,

    /// <summary>It is a value the entity takes, but not one the application will act on right now.</summary>
    Refused,
}

/// <summary>The verdict on one inbound payload, carrying the action to run when it is accepted.</summary>
/// <param name="Outcome">What the entity made of the payload.</param>
/// <param name="Detail">The application's own words for a refusal, carried verbatim to
/// <see cref="MqttConnectionSetup.CommandRefused"/>. The module never composes this: only the
/// application knows why a value it understands is one it will not act on, and a sentence assembled
/// here would be a guess in the module's voice.</param>
/// <param name="Run">The work to do, run on the command worker rather than on the receive callback.
/// Asynchronous and cancellable because the work is the application's — a hypervisor call, a device
/// write — and a teardown must be able to stop waiting on it.</param>
public readonly record struct MqttCommandVerdict(
    MqttCommandOutcome Outcome, string Detail = "", Func<CancellationToken, Task>? Run = null)
{
    public static MqttCommandVerdict Accept(Func<CancellationToken, Task> run) =>
        new(MqttCommandOutcome.Accepted, "", run);

    /// <summary>Accepts work that has nothing to await — a flag flipped, a value stored.</summary>
    public static MqttCommandVerdict Accept(Action run) =>
        new(MqttCommandOutcome.Accepted, "", _ => { run(); return Task.CompletedTask; });

    public static MqttCommandVerdict Malformed(string detail = "") => new(MqttCommandOutcome.Malformed, detail);

    public static MqttCommandVerdict OutOfRange(string detail = "") => new(MqttCommandOutcome.OutOfRange, detail);

    public static MqttCommandVerdict NotAnOption(string detail = "") => new(MqttCommandOutcome.NotAnOption, detail);

    /// <summary>Understood, and declined. <paramref name="detail"/> is the application's wording.</summary>
    public static MqttCommandVerdict Refuse(string detail) => new(MqttCommandOutcome.Refused, detail);

    public bool IsAccepted => Outcome == MqttCommandOutcome.Accepted && Run is not null;
}

/// <summary>One command topic suffix and the handler that runs it. <see cref="Accept"/> parses and
/// validates against the application's own bounds on the receive callback, and returns either a
/// refusal carrying a reason or the work to run.</summary>
public sealed record MqttCommandTarget(string EntityId, Func<string, MqttCommandVerdict> Accept);

/// <summary>Where one inbound message landed: which entity it addressed, and the verdict. A null
/// <see cref="EntityId"/> means the topic was not a command topic at all.</summary>
public readonly record struct MqttCommandRouting(string? EntityId, MqttCommandVerdict Verdict);

/// <summary>One command that was not acted on, as the application's own sink receives it. The
/// module supplies the facts; the wording in <see cref="Detail"/> is whatever the entity put
/// there, which for a module-level outcome is nothing.</summary>
public readonly record struct MqttCommandRefusal(
    DateTimeOffset When, string EntityId, MqttCommandOutcome Outcome, string Detail);

/// <summary>Topic to target. Drops a retained inbound message outright and records the drop.</summary>
/// <remarks>A command is an event, not state. With a clean session plus resubscribe-on-connect, a
/// retained payload under the command subtree would be redelivered and re-fire on every
/// reconnect.</remarks>
public sealed class MqttCommandRouter
{
    private readonly Lock _lock = new();
    private Dictionary<string, MqttCommandTarget> _targets = new(StringComparer.Ordinal);

    public MqttCommandRouter(IEnumerable<MqttCommandTarget> targets) => Replace(targets);

    public IReadOnlyList<string> EntityIds
    {
        get { lock (_lock) return [.. _targets.Keys]; }
    }

    /// <summary>Swaps the declared targets, so an entity set replaced at runtime routes to the new
    /// handlers from the next message on. A whole-dictionary swap, because the receive callback reads
    /// it without holding the lock for the duration of a handler.</summary>
    public void Replace(IEnumerable<MqttCommandTarget> targets)
    {
        var next = new Dictionary<string, MqttCommandTarget>(StringComparer.Ordinal);
        foreach (var target in targets) next[target.EntityId] = target;
        lock (_lock) _targets = next;
    }

    public MqttCommandTarget? Find(string entityId)
    {
        lock (_lock) return _targets.GetValueOrDefault(entityId);
    }

    /// <summary>Resolves and judges one inbound message. Never throws and never runs the work: the
    /// caller enqueues it onto the worker.</summary>
    public MqttCommandRouting Route(
        string topicRoot, string deviceId, string topic, bool retained, string payload)
    {
        if (MqttTopics.CommandEntityId(topicRoot, deviceId, topic) is not { } entityId)
            return new(null, default);

        if (retained)
            return new(entityId, new MqttCommandVerdict(MqttCommandOutcome.Retained));

        if (Find(entityId) is not { } target)
            return new(entityId, new MqttCommandVerdict(MqttCommandOutcome.Unrecognised));

        return new(entityId, target.Accept(payload ?? ""));
    }
}
