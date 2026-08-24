namespace ZeroZero.Mqtt;

/// <summary>Pure rendering of the status lines a settings panel shows, in the module's own en-GB.
/// Callers pass the current instant in rather than the formatter reading a clock, so the wording is
/// testable without one, and every probe sentence is composed here rather than beside the socket
/// that produced it.</summary>
/// <remarks>The static face of <see cref="MqttPanelText.Default"/>. A caller with a translation to
/// apply builds a <see cref="MqttPanelText"/> over its own string source instead; everything else
/// wants the module's own wording, and this is the shorter way to ask for it.</remarks>
public static class MqttStatusText
{
    /// <summary>"just now" / "5 min ago" / "3 hours ago" / "2 days ago", or <paramref name="never"/>
    /// when there is nothing to render. A future timestamp reads as "just now", not a negative age.</summary>
    public static string Relative(DateTimeOffset? when, DateTimeOffset now, string never) =>
        MqttPanelText.Default.Relative(when, now, never);

    /// <summary>The "Broker in use" line: the saved host with whatever port and transport are in
    /// force, whether pinned by hand or found by probing. Pure.</summary>
    public static string DescribeBroker(MqttEndpointRequest request, MqttEndpointMemory? memory) =>
        MqttPanelText.Default.DescribeBroker(request, memory);

    /// <summary>The "Last publish" line.</summary>
    public static string DescribeLastPublish(DateTimeOffset? last, DateTimeOffset now) =>
        MqttPanelText.Default.DescribeLastPublish(last, now);

    /// <summary>The "Last command received" line — which command, and how long ago.</summary>
    public static string DescribeLastCommand(
        MqttCommandRecord? last, DateTimeOffset now, Func<string, string>? label = null) =>
        MqttPanelText.Default.DescribeLastCommand(last, now, label);

    /// <summary>How a transport is named to the user.</summary>
    public static string Name(MqttTransport transport) => MqttPanelText.Default.Name(transport);

    /// <summary>How a connection state is named to the user.</summary>
    public static string Name(MqttConnectionState state) => MqttPanelText.Default.Name(state);

    /// <summary>The sentence shown for one transport's result.</summary>
    public static string Describe(MqttProbeResult result, MqttTransport transport) =>
        MqttPanelText.Default.Describe(result, transport);

    /// <summary>What the sweep is doing right now, as a panel shows it under the button that started
    /// it.</summary>
    public static string Describe(MqttSearchProgress progress) =>
        MqttPanelText.Default.Describe(progress);

    /// <summary>The sentence for a whole run.</summary>
    public static string Describe(MqttProbeReport report) => MqttPanelText.Default.Describe(report);

    /// <summary>Whether a result should be shown in the error colour rather than as plain status.</summary>
    public static bool IsFailure(MqttProbeReport report) => MqttPanelText.IsFailure(report);
}
