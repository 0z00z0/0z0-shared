using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The walk the WinUI loader runs over the resource indexes: which map answers a module
/// string first. The shapes are the ones a consuming application's merged index and the library's
/// own <c>.pri</c> have — the application's entries under the bare map name, the library's under the
/// library's name, and the library's own file under the bare name again.</summary>
/// <remarks>The loader that opens those indexes is Windows-only and needs a desktop session, so it
/// keeps nothing but the opening: the ordering and the walk live in <see cref="MqttResourceMaps"/>
/// and these tests execute them rather than restating them.</remarks>
public class MqttResourceMapsTests
{
    private const string Library = "ZeroZero.Mqtt.WinUI";
    private const string Map = "Resources";

    /// <summary>The index root, which the loader asks after every named subtree.</summary>
    private const string Root = "";

    /// <summary>One resource index: what it answers under each subtree path.</summary>
    private static Dictionary<string, Dictionary<string, string>> Index(
        params (string Subtree, string Key, string Value)[] entries)
    {
        Dictionary<string, Dictionary<string, string>> index = new(StringComparer.Ordinal);
        foreach ((string subtree, string key, string value) in entries)
        {
            if (!index.TryGetValue(subtree, out var map))
                index[subtree] = map = new Dictionary<string, string>(StringComparer.Ordinal);
            map[key] = value;
        }
        return index;
    }

    /// <summary>The probe list the loader builds: for each index in turn, the subtrees it holds in
    /// the order the module states, then the index root itself.</summary>
    private static List<Func<string, string?>> Probes(
        params Dictionary<string, Dictionary<string, string>>[] indexes)
    {
        List<Func<string, string?>> probes = [];
        foreach (var index in indexes)
        {
            foreach (string path in MqttResourceMaps.Subtrees(Library, Map))
                if (index.TryGetValue(path, out var map)) probes.Add(key => map.GetValueOrDefault(key));

            if (index.TryGetValue(Root, out var root)) probes.Add(key => root.GetValueOrDefault(key));
        }
        return probes;
    }

    /// <summary>A consuming application's merged index: its own translation of one key under the
    /// bare map, the library's full set under the library's name.</summary>
    private static Dictionary<string, Dictionary<string, string>> MergedApplicationIndex() =>
        Index(
            (Map, "ButtonApply", "HOST-OVERRIDE"),
            ($"{Library}/{Map}", "ButtonApply", "Apply"),
            ($"{Library}/{Map}", "ButtonTest", "Test connection"));

    /// <summary>The library's own <c>.pri</c> beside the executable, which files its strings under
    /// the bare map name — the same name the application's own entries take in its index.</summary>
    private static Dictionary<string, Dictionary<string, string>> LibraryOwnIndex() =>
        Index((Map, "ButtonApply", "Apply"), (Map, "ButtonTest", "Test connection"));

    [Fact]
    public void AHostsOwnEntryOutranksTheLibrarysCopy() =>
        Assert.Equal("HOST-OVERRIDE", MqttResourceMaps.Find("ButtonApply", Probes(MergedApplicationIndex())));

    [Fact]
    public void AKeyTheHostDoesNotSupplyFallsToTheLibrarysMap() =>
        Assert.Equal("Test connection", MqttResourceMaps.Find("ButtonTest", Probes(MergedApplicationIndex())));

    /// <summary>The case that regresses silently: both indexes file the key under the bare map, so
    /// only the order the two are asked in decides which wording the panel renders.</summary>
    [Fact]
    public void TheApplicationsIndexOutranksTheLibrarysOwnFile() =>
        Assert.Equal(
            "HOST-OVERRIDE",
            MqttResourceMaps.Find("ButtonApply", Probes(MergedApplicationIndex(), LibraryOwnIndex())));

    [Fact]
    public void AKeyOnlyTheLibrarysOwnFileHoldsIsStillFound() =>
        Assert.Equal(
            "Test connection",
            MqttResourceMaps.Find(
                "ButtonTest",
                Probes(Index((Map, "ButtonApply", "HOST-OVERRIDE")), LibraryOwnIndex())));

    /// <summary>The library's map outranks its bare name, so an index that answers under both is
    /// read from the map rather than from whatever sits at the level above it.</summary>
    [Fact]
    public void TheLibrarysMapOutranksItsBareName() =>
        Assert.Equal(
            "Apply",
            MqttResourceMaps.Find(
                "ButtonApply",
                Probes(Index(($"{Library}/{Map}", "ButtonApply", "Apply"), (Library, "ButtonApply", "STALE")))));

    /// <summary>The root is the last resort within an index, not the first.</summary>
    [Fact]
    public void ASubtreeOutranksTheIndexRoot() =>
        Assert.Equal(
            "HOST-OVERRIDE",
            MqttResourceMaps.Find(
                "ButtonApply",
                Probes(Index((Map, "ButtonApply", "HOST-OVERRIDE"), (Root, "ButtonApply", "Apply")))));

    [Fact]
    public void AnIndexHoldingItsItemsDirectlyUnderTheRootStillAnswers() =>
        Assert.Equal("Apply", MqttResourceMaps.Find("ButtonApply", Probes(Index((Root, "ButtonApply", "Apply")))));

    [Fact]
    public void AProbeThatThrowsCountsAsNotHavingTheKey() =>
        Assert.Equal(
            "Apply",
            MqttResourceMaps.Find(
                "ButtonApply",
                [_ => throw new InvalidOperationException(), _ => "Apply"]));

    /// <summary>An empty answer is passed over, so the built-in en-GB fills the control rather than
    /// the panel rendering a blank one.</summary>
    [Fact]
    public void AnEmptyAnswerIsNotAnAnswer() =>
        Assert.Equal("Apply", MqttResourceMaps.Find("ButtonApply", [_ => "", _ => "Apply"]));

    [Fact]
    public void AKeyNoIndexHoldsAnswersNothing() =>
        Assert.Null(MqttResourceMaps.Find("ButtonMissing", Probes(MergedApplicationIndex(), LibraryOwnIndex())));

    [Fact]
    public void NoIndexAtAllAnswersNothing() => Assert.Null(MqttResourceMaps.Find("ButtonApply", []));
}
