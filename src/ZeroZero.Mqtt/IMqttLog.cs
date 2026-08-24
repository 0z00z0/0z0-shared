namespace ZeroZero.Mqtt;

/// <summary>Where the module says what it did. The host owns the logging framework; this is the
/// whole of what the module needs from it.</summary>
public interface IMqttLog
{
    void Info(string message);

    /// <summary>An exception is passed whole so the host can decide how much of it to record. The
    /// module sanitises first — type and message only — so no staged credential reaches here.</summary>
    void Error(string source, Exception? ex);
}

/// <summary>The default: nothing is recorded, so a consumer that has not supplied a log still runs.</summary>
public sealed class NullMqttLog : IMqttLog
{
    public static readonly NullMqttLog Instance = new();

    private NullMqttLog() { }

    public void Info(string message) { }

    public void Error(string source, Exception? ex) { }
}
