using Microsoft.Windows.ApplicationModel.Resources;

namespace ZeroZero.Mqtt.WinUI;

/// <summary>The module's own <c>.resw</c>, read through the Windows App SDK resource loader.</summary>
/// <remarks>
/// <para>Every lookup that finds nothing answers null, which is what leaves the built-in en-GB in
/// <see cref="MqttStrings"/> standing. That matters more for a class library than for an
/// application: where a library's <c>.resw</c> lands in the index an application loads at runtime is
/// the application's build configuration, not this assembly's. A host whose build does not merge it
/// gets an untranslated panel rather than a panel of blank controls, which is why no static text on
/// the panel is bound by <c>x:Uid</c>.</para>
/// <para>Several maps are tried rather than one, because the shape differs by how the consumer
/// builds. A merged application index files the application's own strings under the bare
/// <c>Resources</c> map and the library's under the assembly's name; the library's own <c>.pri</c>
/// beside the executable holds them under the bare name. This class only opens the indexes and lists
/// the places to ask, host-owned first; <see cref="MqttResourceMaps"/> owns both the order and the
/// walk, in a plain <c>net10.0</c> assembly a test can run.</para>
/// <para>A consumer localises by adding a language folder alongside <c>en-GB</c>, or by supplying an
/// <see cref="IMqttStringSource"/> of its own on the panel's setup object. Nothing here is forked and
/// no package is added.</para>
/// </remarks>
public sealed class MqttResourceStrings : IMqttStringSource
{
    /// <summary>The assembly's own name: the <c>.pri</c> file's stem, and the map a merged index
    /// files the library's resources under.</summary>
    private const string Library = "ZeroZero.Mqtt.WinUI";

    /// <summary>The <c>.resw</c> file's own name, which is the map below the root.</summary>
    private const string Map = "Resources";

    /// <summary>The module's strings, or a source that answers nothing when no index can be opened.</summary>
    public static IMqttStringSource Instance { get; } = new MqttResourceStrings();

    /// <summary>The places to ask, in precedence order.</summary>
    private readonly List<Func<string, string?>> _probes = [];

    private MqttResourceStrings()
    {
        // The application's own index first: a host that translates these strings itself expects its
        // map to win over the copy shipped beside the library.
        Collect(() => new ResourceManager());
        Collect(() => new ResourceManager($"{Library}.pri"));
    }

    private void Collect(Func<ResourceManager> open)
    {
        ResourceMap root;
        try
        {
            root = open().MainResourceMap;
        }
        catch (Exception)
        {
            // No index of that name, or none at all. The built-in en-GB is the whole fallback and it
            // is complete, so there is nothing to report and nothing to degrade.
            return;
        }

        foreach (string path in MqttResourceMaps.Subtrees(Library, Map))
            if (Subtree(root, path) is { } map) _probes.Add(key => map.TryGetValue(key)?.ValueAsString);

        // The root itself, for an index whose items sit directly under it.
        _probes.Add(key => root.TryGetValue(key)?.ValueAsString);
    }

    private static ResourceMap? Subtree(ResourceMap root, string path)
    {
        try { return root.TryGetSubtree(path); }
        catch (Exception) { return null; }
    }

    public string? Find(string key) => MqttResourceMaps.Find(key, _probes);
}
