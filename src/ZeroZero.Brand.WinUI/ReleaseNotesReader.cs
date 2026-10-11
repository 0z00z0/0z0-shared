namespace ZeroZero.Brand.WinUI;

/// <summary>The release-notes download behind "What's new", free of WinUI so a test can run it.</summary>
internal static class ReleaseNotesReader
{
    /// <summary>Also caps what an address under someone else's control can make the control download.</summary>
    internal const int MaxCharacters = 4000;

    internal const string NothingToShow = "There is nothing to show for this release.";

    /// <summary>Reads the body as it arrives, at most <see cref="MaxCharacters"/>; a full buffer ends in "…".
    /// Throws on a failed request.</summary>
    internal static async Task<string> FetchAsync(HttpClient http, string url, CancellationToken token)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(body);

        var buffer = new char[MaxCharacters];
        int read = await reader.ReadBlockAsync(buffer, token);
        string text = new string(buffer, 0, read).TrimEnd();

        if (text.Length == 0) return NothingToShow;

        return read == buffer.Length ? text + "…" : text;
    }
}
