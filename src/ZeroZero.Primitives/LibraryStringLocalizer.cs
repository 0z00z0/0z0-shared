using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace ZeroZero.Primitives;

/// <summary>A component's own texts for the current UI culture, the host's localizer asked first. A
/// host answer that is not found or is blank counts as missing, never as an empty text. A host text
/// that cannot be formatted with the component's arguments gives way to the component's own.</summary>
public sealed class LibraryStringLocalizer(ResourceManager resources, IStringLocalizer? host = null) : IStringLocalizer
{
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);

    /// <summary>Told once per key when a text cannot be formatted. Optional; nothing is told without it.</summary>
    public ILogSink? Log { get; init; }

    public LocalizedString this[string name] =>
        Hosted(name) is { } answer
            ? new(name, answer)
            : resources.GetString(name, CultureInfo.CurrentUICulture) is { } own
                ? new(name, own)
                : new(name, name, resourceNotFound: true);

    /// <summary>The UI culture picks the text; numbers, sizes and times inserted into it follow the
    /// regional format, the current culture with the person's own changes. A caller that inserts an
    /// identifier such as a port or a version formats it invariantly itself.</summary>
    public LocalizedString this[string name, params object[] arguments] =>
        Format(name, pattern => string.Format(CultureInfo.CurrentCulture, pattern, arguments));

    /// <summary>The text for <paramref name="name"/> passed through <paramref name="format"/>. A host
    /// text the formatter rejects gives way to the component's own; one the formatter still rejects
    /// comes back unformatted. Never throws.</summary>
    public LocalizedString Format(string name, Func<string, string> format)
    {
        if (Hosted(name) is { } answer)
        {
            try { return new(name, format(answer)); }
            catch (Exception ex) { Report(name, ex); }
        }

        if (resources.GetString(name, CultureInfo.CurrentUICulture) is not { } own)
            return new(name, name, resourceNotFound: true);

        try { return new(name, format(own)); }
        catch (Exception ex)
        {
            Report(name, ex);
            return new(name, own);
        }
    }

    /// <summary>Every key the component declares, each answered as the indexer answers it.</summary>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => this[(string)entry.Key]);

    private string? Hosted(string name) =>
        host?[name] is { ResourceNotFound: false, Value: var answer } && !string.IsNullOrWhiteSpace(answer) ? answer : null;

    private void Report(string name, Exception ex)
    {
        if (Log is { } log && _reported.TryAdd(name, 0))
            log.Error($"LibraryStringLocalizer: text '{name}' cannot be formatted with the arguments given", ex);
    }
}
