using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The walk the WinUI loader runs over the resource indexes: which map answers a module
/// string first. The shapes are the ones a consuming application's merged index and the library's
/// own <c>.pri</c> have — the application's entries under the bare map name, the library's under the
/// library's name, and the library's own file under the bare name again.</summary>
/// <remarks>The loader that opens those indexes is Windows-only and needs a desktop session, so it
/// keeps nothing but the opening: which indexes, which maps under them, both orders and the walk
/// live in <see cref="MqttResourceMaps"/> and these tests execute them rather than restating them.
/// The indexes below are therefore written in an order of their own and walked in the module's, so a
/// reversal there fails an assertion here.</remarks>
public class MqttResourceMapsTests
{
    private const string Library = "ZeroZero.Mqtt.WinUI";
    private const string Map = "Resources";

    /// <summary>The index root, which the loader asks after every named subtree.</summary>
    private const string Root = "";

    /// <summary>The index a merged application build produces, which the loader opens by asking for
    /// no file.</summary>
    private const string ApplicationIndex = MqttResourceMaps.ApplicationIndex;

    /// <summary>The library's own file beside the executable, written out rather than composed, so a
    /// change to how the module forms that name fails here.</summary>
    private const string LibraryIndex = "ZeroZero.Mqtt.WinUI.pri";

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

    /// <summary>The probe list the loader builds: the named indexes that exist, taken in the order
    /// the module opens them rather than the order they are written here, and inside each the
    /// subtrees the module states, root last. An index the module never opens is never asked.</summary>
    private static List<Func<string, string?>> Probes(
        params (string Name, Dictionary<string, Dictionary<string, string>> Content)[] indexes)
    {
        List<Func<string, string?>> probes = [];
        foreach (string name in MqttResourceMaps.Indexes(Library))
            foreach ((string candidate, var content) in indexes)
                if (string.Equals(candidate, name, StringComparison.Ordinal)) probes.AddRange(ProbesIn(content));

        return probes;
    }

    /// <summary>One index's probes: the subtrees it holds in the order the module states, then the
    /// index root itself.</summary>
    private static List<Func<string, string?>> ProbesIn(Dictionary<string, Dictionary<string, string>> index)
    {
        List<Func<string, string?>> probes = [];
        foreach (string path in MqttResourceMaps.Subtrees(Library, Map))
            if (index.TryGetValue(path, out var map)) probes.Add(key => map.GetValueOrDefault(key));

        if (index.TryGetValue(Root, out var root)) probes.Add(key => root.GetValueOrDefault(key));
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

    /// <summary>The two indexes the loader opens, and the order it opens them in — the pair of lines
    /// that used to sit in the loader itself, where nothing could run them.</summary>
    [Fact]
    public void TheApplicationsIndexIsOpenedBeforeTheLibrarysOwnFile() =>
        Assert.Equal(new[] { ApplicationIndex, LibraryIndex }, MqttResourceMaps.Indexes(Library));

    /// <summary>The application's own index is named by no file at all, which is the branch the
    /// loader takes to open the index the application already has rather than a file beside it.</summary>
    [Fact]
    public void TheApplicationsIndexIsNamedByNoFile() => Assert.Empty(MqttResourceMaps.ApplicationIndex);

    [Fact]
    public void AHostsOwnEntryOutranksTheLibrarysCopy() =>
        Assert.Equal(
            "HOST-OVERRIDE",
            MqttResourceMaps.Find("ButtonApply", Probes((ApplicationIndex, MergedApplicationIndex()))));

    [Fact]
    public void AKeyTheHostDoesNotSupplyFallsToTheLibrarysMap() =>
        Assert.Equal(
            "Test connection",
            MqttResourceMaps.Find("ButtonTest", Probes((ApplicationIndex, MergedApplicationIndex()))));

    /// <summary>The case that regresses silently: both indexes file the key under the bare map, so
    /// only the order the two are opened in decides which wording the panel renders. The library's
    /// index is written first here and still asked second, because the module's order decides.</summary>
    [Fact]
    public void TheApplicationsIndexOutranksTheLibrarysOwnFile() =>
        Assert.Equal(
            "HOST-OVERRIDE",
            MqttResourceMaps.Find(
                "ButtonApply",
                Probes((LibraryIndex, LibraryOwnIndex()), (ApplicationIndex, MergedApplicationIndex()))));

    [Fact]
    public void AKeyOnlyTheLibrarysOwnFileHoldsIsStillFound() =>
        Assert.Equal(
            "Test connection",
            MqttResourceMaps.Find(
                "ButtonTest",
                Probes(
                    (ApplicationIndex, Index((Map, "ButtonApply", "HOST-OVERRIDE"))),
                    (LibraryIndex, LibraryOwnIndex()))));

    /// <summary>The library's map outranks its bare name, so an index that answers under both is
    /// read from the map rather than from whatever sits at the level above it.</summary>
    [Fact]
    public void TheLibrarysMapOutranksItsBareName() =>
        Assert.Equal(
            "Apply",
            MqttResourceMaps.Find(
                "ButtonApply",
                Probes((
                    ApplicationIndex,
                    Index(($"{Library}/{Map}", "ButtonApply", "Apply"), (Library, "ButtonApply", "STALE"))))));

    /// <summary>The root is the last resort within an index, not the first.</summary>
    [Fact]
    public void ASubtreeOutranksTheIndexRoot() =>
        Assert.Equal(
            "HOST-OVERRIDE",
            MqttResourceMaps.Find(
                "ButtonApply",
                Probes((
                    ApplicationIndex,
                    Index((Map, "ButtonApply", "HOST-OVERRIDE"), (Root, "ButtonApply", "Apply"))))));

    [Fact]
    public void AnIndexHoldingItsItemsDirectlyUnderTheRootStillAnswers() =>
        Assert.Equal(
            "Apply",
            MqttResourceMaps.Find(
                "ButtonApply",
                Probes((ApplicationIndex, Index((Root, "ButtonApply", "Apply"))))));

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
        Assert.Null(
            MqttResourceMaps.Find(
                "ButtonMissing",
                Probes((ApplicationIndex, MergedApplicationIndex()), (LibraryIndex, LibraryOwnIndex()))));

    [Fact]
    public void NoIndexAtAllAnswersNothing() => Assert.Null(MqttResourceMaps.Find("ButtonApply", []));
}
