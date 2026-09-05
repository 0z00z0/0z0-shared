namespace ZeroZero.Mqtt;

/// <summary>Where, under one resource index, the module's strings are looked for, in the order they
/// are asked, and the walk that asks them. The order is the rule: a host's own map before the
/// library's copy, so a consumer's <c>.resw</c> entry for a module key is the one the panel
/// renders.</summary>
/// <remarks>A merged application index files the application's own resources under the bare map
/// name and a referenced library's under the library's name; the library's own <c>.pri</c> holds
/// its strings under the bare name. Asked in this order, one walk reads the host's entry from the
/// application's index and the library's from either. Plain <c>net10.0</c>, so a test drives the
/// walk itself with no resource system behind it — the loader that opens the indexes is Windows-only
/// and cannot be loaded where the tests run.</remarks>
public static class MqttResourceMaps
{
    /// <summary>The subtrees to ask under an index root, host-owned first.</summary>
    public static IReadOnlyList<string> Subtrees(string library, string map) =>
        [map, $"{library}/{map}", library];

    /// <summary>The first answer to <paramref name="key"/> from <paramref name="probes"/>, asked in
    /// order, or null where none of them holds it. The caller's order is the precedence.</summary>
    public static string? Find(string key, IEnumerable<Func<string, string?>> probes)
    {
        foreach (var probe in probes)
        {
            string? value;

            // A place that throws on a key rather than answering null is still a place without it.
            try { value = probe(key); }
            catch (Exception) { continue; }

            // An empty answer is no answer: taking it would blank a control the built-in text fills.
            if (value is { Length: > 0 }) return value;
        }

        return null;
    }
}
