namespace ZeroZero.Mqtt;

/// <summary>The most recent inbound command: which entity it addressed and when it landed.</summary>
public sealed record MqttCommandRecord(DateTimeOffset When, string EntityId);

/// <summary>When something last reached the broker, and what the broker last asked for.</summary>
/// <remarks>
/// <para>An instance owned by one connection, never a static. A consumer detects a rebuilt
/// connection by identity — the same instance means the same session — and a process-wide static
/// would make two connections indistinguishable and leave one reporting the other's traffic.</para>
/// <para>Written from the MQTT threads and read from a UI thread, so each slot is swapped
/// atomically: the command as a whole record, so a reader never pairs one command's timestamp with
/// another's entity.</para>
/// </remarks>
public sealed class MqttActivity
{
    private long _lastPublishTicks;   // UTC ticks; 0 = nothing published yet
    private MqttCommandRecord? _lastCommand;

    /// <summary>Records a publish the broker actually acknowledged. Call only on success.</summary>
    public void RecordPublish() => RecordPublish(DateTimeOffset.UtcNow);

    /// <summary>Records a publish at a given instant, so a test pins the age without waiting.</summary>
    public void RecordPublish(DateTimeOffset when) =>
        Interlocked.Exchange(ref _lastPublishTicks, when.UtcTicks);

    /// <summary>Records a recognised, accepted inbound command — not a refused or retained payload.</summary>
    public void RecordCommand(string entityId) => RecordCommand(entityId, DateTimeOffset.UtcNow);

    public void RecordCommand(string entityId, DateTimeOffset when) =>
        Volatile.Write(ref _lastCommand, new MqttCommandRecord(when, entityId));

    public DateTimeOffset? LastPublish =>
        Interlocked.Read(ref _lastPublishTicks) is var ticks && ticks != 0
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;

    public MqttCommandRecord? LastCommand => Volatile.Read(ref _lastCommand);
}
