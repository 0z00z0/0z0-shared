using ZeroZero.Primitives;

namespace ZeroZero.Mqtt;

/// <summary>The shared log sink under the module's own name, so a host's implementation of this is
/// a sink any component takes. The module itself is typed on <see cref="ILogSink"/> throughout.</summary>
public interface IMqttLog : ILogSink
{
}

/// <summary>The no-op under the module's own name; <see cref="NullLogSink.Instance"/> is what the
/// module defaults to.</summary>
public sealed class NullMqttLog : IMqttLog
{
    public static readonly NullMqttLog Instance = new();

    private NullMqttLog() { }

    public void Info(string message) { }

    public void Error(string source, Exception? ex) { }
}
