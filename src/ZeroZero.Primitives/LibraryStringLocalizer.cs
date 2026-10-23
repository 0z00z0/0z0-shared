using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace ZeroZero.Primitives;

/// <summary>A component's own texts for the current UI culture, the host's localizer asked first. A
/// host answer that is not found or is blank counts as missing, never as an empty text.</summary>
public sealed class LibraryStringLocalizer(ResourceManager resources, IStringLocalizer? host = null) : IStringLocalizer
{
    public LocalizedString this[string name] =>
        host?[name] is { ResourceNotFound: false, Value: var answer } && !string.IsNullOrWhiteSpace(answer)
            ? new(name, answer)
            : resources.GetString(name, CultureInfo.CurrentUICulture) is { } own
                ? new(name, own)
                : new(name, name, resourceNotFound: true);

    /// <summary>The UI culture picks the text; numbers, sizes and times inserted into it follow the
    /// regional format, the current culture with the person's own changes. A caller that inserts an
    /// identifier such as a port or a version formats it invariantly itself.</summary>
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var text = this[name];
            return new(name, string.Format(CultureInfo.CurrentCulture, text.Value, arguments), text.ResourceNotFound);
        }
    }

    /// <summary>Every key the component declares, each answered as the indexer answers it.</summary>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => this[(string)entry.Key]);
}
