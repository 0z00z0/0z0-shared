using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ZeroZero.Controls.WinUI;

/// <summary>An application's own <c>.resw</c> entries as a localizer a library asks before its own
/// texts. An index that fails to open leaves fewer places to ask, never an exception.</summary>
public sealed class ResourceMapStringLocalizer : IStringLocalizer
{
    private const string Map = "Resources";

    private readonly List<Func<string, string?>> _probes = [];

    /// <param name="library">The library's assembly name: its <c>.pri</c> stem and its map in a merged index.</param>
    public ResourceMapStringLocalizer(string library)
    {
        foreach (string index in ResourceMapWalk.Indexes(library)) Collect(index, library);
    }

    private void Collect(string index, string library)
    {
        ResourceMap root;
        try
        {
            ResourceManager manager =
                index == ResourceMapWalk.ApplicationIndex ? new ResourceManager() : new ResourceManager(index);
            root = manager.MainResourceMap;
        }
        catch (Exception)
        {
            return;
        }

        foreach (string path in ResourceMapWalk.Subtrees(library, Map))
            if (Subtree(root, path) is { } map) _probes.Add(key => map.TryGetValue(key)?.ValueAsString);

        // The root last, for an index whose items sit directly under it.
        _probes.Add(key => root.TryGetValue(key)?.ValueAsString);
    }

    private static ResourceMap? Subtree(ResourceMap root, string path)
    {
        try { return root.TryGetSubtree(path); }
        catch (Exception) { return null; }
    }

    public LocalizedString this[string name] =>
        ResourceMapWalk.Find(name, _probes) is { } value
            ? new(name, value)
            : new(name, name, resourceNotFound: true);

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var text = this[name];
            return new(name, string.Format(CultureInfo.CurrentUICulture, text.Value, arguments), text.ResourceNotFound);
        }
    }

    /// <summary>Empty: a merged index cannot list one library's keys.</summary>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
