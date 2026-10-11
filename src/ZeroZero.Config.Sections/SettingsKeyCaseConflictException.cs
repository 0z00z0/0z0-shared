namespace ZeroZero.Config.Sections;

/// <summary>A write refused because it would leave two keys differing only in case: a reader takes the
/// last, silently retiring the other and its values. The message names both; nothing is written.</summary>
public sealed class SettingsKeyCaseConflictException : InvalidOperationException
{
    internal SettingsKeyCaseConflictException(string wanted, string found, string where)
        : base($"Writing '{wanted}' {where} would put it beside '{found}', which differs from it only in case. A reader takes the last of two such keys, so the other one would stop governing anything. Nothing has been written.")
    {
        Wanted = wanted;
        Found = found;
    }

    /// <summary>The name this build wanted to write.</summary>
    public string Wanted { get; }

    /// <summary>The name the document already carries, differing only in case.</summary>
    public string Found { get; }
}
