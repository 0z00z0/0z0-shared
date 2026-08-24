using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZeroZero.Mqtt.Discovery;

/// <summary>One component entry under construction. A key with nothing to say is not written at all,
/// so an omitted property leaves the receiver its own default rather than an explicit null.</summary>
/// <remarks>Insertion order is kept, which is what lets a golden test read a composed entry the way
/// it was declared.</remarks>
internal sealed class DiscoveryKeys(JsonObject target)
{
    public void Set(string key, string? value)
    {
        if (value is { Length: > 0 }) target[key] = value;
    }

    public void Set(string key, double? value)
    {
        if (value is { } number) target[key] = number;
    }

    public void Set(string key, int? value)
    {
        if (value is { } number) target[key] = number;
    }

    /// <summary>A key whose null is a value rather than an absence: it is written either way, as the
    /// string or as JSON null. What lets an entity declare itself the device's main feature.</summary>
    public void SetOrNull(string key, string? value) => target[key] = value;

    /// <summary>Written only when true: a false flag is the receiver's own default everywhere this
    /// model uses one.</summary>
    public void SetWhenTrue(string key, bool value)
    {
        if (value) target[key] = true;
    }

    /// <summary>Written only when false, for a key whose receiver-side default is true.</summary>
    public void SetWhenFalse(string key, bool value)
    {
        if (!value) target[key] = false;
    }

    public void SetList(string key, IEnumerable<string> values) =>
        target[key] = new JsonArray([.. values.Select(v => (JsonNode)JsonValue.Create(v))]);

    /// <summary>An escape-hatch value of whatever shape the consumer declared. Serialised through the
    /// same writer as everything else, so a string, a number and a nested object all land as JSON
    /// rather than as a quoted <c>ToString</c>.</summary>
    public void SetRaw(string key, object? value) =>
        target[key] = value is null ? null : JsonSerializer.SerializeToNode(value);
}
