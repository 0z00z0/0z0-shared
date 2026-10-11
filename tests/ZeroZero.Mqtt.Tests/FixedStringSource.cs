namespace ZeroZero.Mqtt.Tests;

/// <summary>A translation answering from a fixed table, and nothing for any other key.</summary>
internal sealed class FixedStringSource(Dictionary<string, string> entries) : IMqttStringSource
{
    public string? Find(string key) => entries.GetValueOrDefault(key);
}
