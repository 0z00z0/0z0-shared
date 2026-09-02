namespace ZeroZero.Mqtt;

/// <summary>Where, under one resource index, the module's strings are looked for, in the order
/// they are asked. The order is the rule: a host's own map before the library's copy, so a
/// consumer's <c>.resw</c> entry for a module key is the one the panel renders.</summary>
/// <remarks>A merged application index files the application's own resources under the bare map
/// name and a referenced library's under the library's name; the library's own <c>.pri</c> holds
/// its strings under the bare name. Asked in this order, one walk reads the host's entry from the
/// application's index and the library's from either. Plain <c>net10.0</c>, so a test can pin the
/// order without a resource system behind it.</remarks>
public static class MqttResourceMaps
{
    /// <summary>The subtrees to ask under an index root, host-owned first.</summary>
    public static IReadOnlyList<string> Subtrees(string library, string map) =>
        [map, $"{library}/{map}", library];
}
