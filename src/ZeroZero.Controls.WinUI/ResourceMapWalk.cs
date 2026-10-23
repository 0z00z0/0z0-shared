namespace ZeroZero.Controls.WinUI;

/// <summary>Which resource indexes and subtrees a library key is asked in, application first, and the
/// walk that asks them. Names no WinUI type, so a test drives the precedence.</summary>
internal static class ResourceMapWalk
{
    /// <summary>The application's own index, opened by naming no file.</summary>
    public const string ApplicationIndex = "";

    /// <summary>The indexes to open, application first: this order decides which wording renders.</summary>
    public static IReadOnlyList<string> Indexes(string library) => [ApplicationIndex, $"{library}.pri"];

    public static IReadOnlyList<string> Subtrees(string library, string map) =>
        [map, $"{library}/{map}", library];

    public static string? Find(string key, IEnumerable<Func<string, string?>> probes)
    {
        foreach (var probe in probes)
        {
            string? value;

            // A place that throws on a key is a place without it.
            try { value = probe(key); }
            catch (Exception) { continue; }

            if (value is { Length: > 0 }) return value;
        }

        return null;
    }
}
