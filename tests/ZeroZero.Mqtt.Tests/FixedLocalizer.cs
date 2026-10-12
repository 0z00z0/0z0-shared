using Microsoft.Extensions.Localization;

namespace ZeroZero.Mqtt.Tests;

/// <summary>A host localizer answering from a fixed table, and not found for any other key.</summary>
internal sealed class FixedLocalizer(Dictionary<string, string> entries) : IStringLocalizer
{
    public int Lookups { get; private set; }

    public LocalizedString this[string name]
    {
        get
        {
            Lookups++;
            return entries.TryGetValue(name, out var value) ? new(name, value) : new(name, name, resourceNotFound: true);
        }
    }

    public LocalizedString this[string name, params object[] arguments] => this[name];

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
