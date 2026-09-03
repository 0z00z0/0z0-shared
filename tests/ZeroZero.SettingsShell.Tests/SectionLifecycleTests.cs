using Xunit;
using ZeroZero.SettingsShell.WinUI;

namespace ZeroZero.SettingsShell.Tests;

/// <summary>
/// The two contract points the plan measures the shell on, pinned in order: the enter and leave
/// hooks around every change of section, and the build-once flag a rebuild honours. Pages are
/// strings naming the section and the build that made them, so a rebuilt page is told apart from
/// the one it replaced.
/// </summary>
public class SectionLifecycleTests
{
    /// <summary>Records what the lifecycle asks of the host, in the one log the hooks write to,
    /// so the order between a hook and a visibility change is what is asserted.</summary>
    private sealed class Host : ISectionHost<string>
    {
        public List<string> Log { get; } = [];
        public List<string> Present { get; } = [];
        public HashSet<string> Visible { get; } = [];

        public void Add(string page) { Log.Add($"add {page}"); Present.Add(page); }
        public void Remove(string page) { Log.Add($"remove {page}"); Present.Remove(page); Visible.Remove(page); }
        public void Show(string page) { Log.Add($"show {page}"); Visible.Add(page); }
        public void Hide(string page) { Log.Add($"hide {page}"); Visible.Remove(page); }
    }

    private sealed class Rig
    {
        public Host Host { get; } = new();
        private readonly Dictionary<string, int> _builds = new(StringComparer.Ordinal);

        public SectionPlan<string> Plan(string tag, bool buildOnce = false) => new(
            tag,
            () =>
            {
                int n = _builds[tag] = _builds.GetValueOrDefault(tag) + 1;
                Host.Log.Add($"build {tag}");
                return $"{tag}#{n}";
            },
            () => Host.Log.Add($"enter {tag}"),
            () => Host.Log.Add($"leave {tag}"),
            buildOnce);

        public SectionLifecycle<string> Lifecycle(params SectionPlan<string>[] plans) => new(plans, Host);
    }

    [Fact]
    public void BuildAll_BuildsEveryPageOnceInOrderAndAddsItHidden()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"), rig.Plan("b"));

        lifecycle.BuildAll();

        Assert.Equal(["build a", "add a#1", "build b", "add b#1"], rig.Host.Log);
        Assert.Equal(["a#1", "b#1"], lifecycle.Pages);
        Assert.Empty(rig.Host.Visible);
        Assert.Null(lifecycle.Current);
    }

    [Fact]
    public void BuildAll_Again_BuildsNothing()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"));
        lifecycle.BuildAll();
        rig.Host.Log.Clear();

        lifecycle.BuildAll();

        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    public void FirstSelect_ShowsThePageThenEnters_AndLeavesNothing()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"), rig.Plan("b"));
        lifecycle.BuildAll();
        rig.Host.Log.Clear();

        lifecycle.Select("b");

        Assert.Equal(["show b#1", "enter b"], rig.Host.Log);
        Assert.Equal("b", lifecycle.Current);
    }

    [Fact]
    public void Select_LeavesTheOldHidesItShowsTheNewThenEnters()
    {
        // A hook always sees its own page on screen and never the other's: leave runs while the
        // old page is still visible, enter once the new one is.
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"), rig.Plan("b"));
        lifecycle.BuildAll();
        lifecycle.Select("a");
        rig.Host.Log.Clear();

        lifecycle.Select("b");

        Assert.Equal(["leave a", "hide a#1", "show b#1", "enter b"], rig.Host.Log);
        Assert.Equal(["b#1"], rig.Host.Visible);
        Assert.Equal(["a#1", "b#1"], rig.Host.Present);
    }

    [Fact]
    public void Select_TheCurrentSection_DoesNothing()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"));
        lifecycle.BuildAll();
        lifecycle.Select("a");
        rig.Host.Log.Clear();

        lifecycle.Select("a");

        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    public void Select_BuildsAPageNotBuiltYet()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"));

        lifecycle.Select("a");

        Assert.Equal(["build a", "add a#1", "show a#1", "enter a"], rig.Host.Log);
    }

    [Fact]
    public void Select_AnUnknownTag_IsRefusedAndChangesNothing()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"));
        lifecycle.BuildAll();
        lifecycle.Select("a");
        rig.Host.Log.Clear();

        var ex = Assert.Throws<ArgumentException>(() => lifecycle.Select("nope"));

        Assert.Contains("nope", ex.Message);
        Assert.Equal("a", lifecycle.Current);
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    public void Rebuild_LeavesABuildOnceSectionAloneAndRebuildsTheRest()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("once", buildOnce: true), rig.Plan("again"));
        lifecycle.BuildAll();
        lifecycle.Select("once");
        rig.Host.Log.Clear();

        lifecycle.Rebuild();

        Assert.Equal(["remove again#1", "build again", "add again#2"], rig.Host.Log);
        Assert.Equal(["once#1", "again#2"], lifecycle.Pages);
        Assert.Equal(["once#1"], rig.Host.Visible);
    }

    [Fact]
    public void Rebuild_OfTheCurrentSection_LeavesTheOldPageAndEntersTheNew()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"), rig.Plan("b"));
        lifecycle.BuildAll();
        lifecycle.Select("b");
        rig.Host.Log.Clear();

        lifecycle.Rebuild();

        Assert.Equal(
            ["remove a#1", "build a", "add a#2", "leave b", "remove b#1", "build b", "add b#2", "show b#2", "enter b"],
            rig.Host.Log);
        Assert.Equal("b", lifecycle.Current);
        Assert.Equal(["b#2"], rig.Host.Visible);
    }

    [Fact]
    public void RebuildByTag_RebuildsThatSectionOnly()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"), rig.Plan("b"));
        lifecycle.BuildAll();
        lifecycle.Select("a");
        rig.Host.Log.Clear();

        lifecycle.Rebuild("b");

        Assert.Equal(["remove b#1", "build b", "add b#2"], rig.Host.Log);
        Assert.Equal(["a#1"], rig.Host.Visible);
    }

    [Fact]
    public void RebuildByTag_OfABuildOnceSection_IsRefusedAndChangesNothing()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("once", buildOnce: true));
        lifecycle.BuildAll();
        lifecycle.Select("once");
        rig.Host.Log.Clear();

        var ex = Assert.Throws<InvalidOperationException>(() => lifecycle.Rebuild("once"));

        Assert.Contains("once", ex.Message);
        Assert.Equal(["once#1"], lifecycle.Pages);
        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    public void RebuildByTag_OfAnUnknownSection_IsRefused()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"));

        Assert.Throws<ArgumentException>(() => lifecycle.Rebuild("nope"));
    }

    [Fact]
    public void Close_LeavesTheCurrentSectionAndKeepsEveryPage()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"), rig.Plan("b"));
        lifecycle.BuildAll();
        lifecycle.Select("a");
        rig.Host.Log.Clear();

        lifecycle.Close();

        Assert.Equal(["leave a"], rig.Host.Log);
        Assert.Null(lifecycle.Current);
        Assert.Equal(["a#1", "b#1"], rig.Host.Present);
    }

    [Fact]
    public void Close_WithNothingCurrent_LeavesNothing()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("a"));
        lifecycle.BuildAll();
        rig.Host.Log.Clear();

        lifecycle.Close();

        Assert.Empty(rig.Host.Log);
    }

    [Fact]
    public void HooksAreOptional()
    {
        var host = new Host();
        var lifecycle = new SectionLifecycle<string>(
            [new SectionPlan<string>("a", () => "a", null, null, false),
             new SectionPlan<string>("b", () => "b", null, null, false)],
            host);
        lifecycle.BuildAll();

        lifecycle.Select("a");
        lifecycle.Select("b");
        lifecycle.Rebuild();
        lifecycle.Close();

        Assert.Equal(["add a", "add b", "show a", "hide a", "show b", "remove a", "add a", "remove b", "add b", "show b"], host.Log);
    }

    [Fact]
    public void Tags_AreInDeclarationOrder()
    {
        var rig = new Rig();
        var lifecycle = rig.Lifecycle(rig.Plan("zeta"), rig.Plan("alpha"), rig.Plan("mid"));

        Assert.Equal(["zeta", "alpha", "mid"], lifecycle.Tags);
        Assert.True(lifecycle.Contains("mid"));
        Assert.False(lifecycle.Contains("MID"));
    }

    [Fact]
    public void ABuildThatReturnsNoPage_IsAnError()
    {
        var host = new Host();
        var lifecycle = new SectionLifecycle<string>(
            [new SectionPlan<string>("a", () => null!, null, null, false)], host);

        var ex = Assert.Throws<InvalidOperationException>(lifecycle.BuildAll);

        Assert.Contains("a", ex.Message);
    }

    [Fact]
    public void TwoSectionsWithOneTag_AreRefused()
    {
        var rig = new Rig();

        var ex = Assert.Throws<ArgumentException>(() => rig.Lifecycle(rig.Plan("a"), rig.Plan("a")));

        Assert.Contains("'a'", ex.Message);
    }

    [Fact]
    public void NoSections_AreRefused()
    {
        var rig = new Rig();

        Assert.Throws<ArgumentException>(() => rig.Lifecycle());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankTag_IsRefused(string tag)
    {
        var rig = new Rig();

        Assert.Throws<ArgumentException>(() => rig.Lifecycle(rig.Plan(tag)));
    }

    [Fact]
    public void ASectionWithNoBuild_IsRefused()
    {
        var host = new Host();

        Assert.Throws<ArgumentException>(() => new SectionLifecycle<string>(
            [new SectionPlan<string>("a", null!, null, null, false)], host));
    }
}
