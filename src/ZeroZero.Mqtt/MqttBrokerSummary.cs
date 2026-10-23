namespace ZeroZero.Mqtt;

/// <summary>The closed Broker section's line. <see cref="DetectedAt"/> holds, in ascending order, each
/// position in <see cref="Text"/> straight after a value that was found rather than set by hand, where
/// a panel draws its mark.</summary>
public sealed record MqttBrokerSummary(string Text, IReadOnlyList<int> DetectedAt)
{
    // Private use, so no translation carries it; the composer strips it from the typed host.
    internal const char Mark = '';

    internal static MqttBrokerSummary FromMarked(string marked)
    {
        var text = new System.Text.StringBuilder(marked.Length);
        var detectedAt = new List<int>();
        foreach (char c in marked)
        {
            if (c == Mark) detectedAt.Add(text.Length);
            else text.Append(c);
        }
        return new(text.ToString(), detectedAt);
    }
}
