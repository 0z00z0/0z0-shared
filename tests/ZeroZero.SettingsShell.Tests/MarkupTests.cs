using System.Xml.Linq;
using Xunit;

namespace ZeroZero.SettingsShell.Tests;

/// <summary>
/// The window's markup read as data and held to the chrome the plan measured in both
/// applications: Mica, a left pane with no toggle, no settings item and no back button, the
/// transparency overrides that let the backdrop through, the icon box at 28, one scroll viewer
/// over the page host, and the product footer beneath the pane.
/// </summary>
public class MarkupTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheBackdropIsMicaBaseAlt()
    {
        var mica = Load().Descendants(P + "MicaBackdrop").Single();

        Assert.Equal("BaseAlt", mica.Attribute("Kind")?.Value);
    }

    [Fact]
    public void ThePaneIsLeftAlwaysOpenAndCarriesNoToggleSettingsOrBackButton()
    {
        var navigation = Navigation();

        Assert.Equal("Left", navigation.Attribute("PaneDisplayMode")?.Value);
        Assert.Equal("True", navigation.Attribute("IsPaneOpen")?.Value);
        Assert.Equal("False", navigation.Attribute("IsPaneToggleButtonVisible")?.Value);
        Assert.Equal("False", navigation.Attribute("IsSettingsVisible")?.Value);
        Assert.Equal("Collapsed", navigation.Attribute("IsBackButtonVisible")?.Value);
    }

    [Fact]
    public void ThePaneNeverCollapsesWithTheWindowWidth()
    {
        // With no toggle, a pane that folded away at a narrow width could never be opened again.
        var navigation = Navigation();

        Assert.Equal("0", navigation.Attribute("CompactModeThresholdWidth")?.Value);
        Assert.Equal("0", navigation.Attribute("ExpandedModeThresholdWidth")?.Value);
    }

    [Fact]
    public void NoHeaderBandSitsAboveThePages()
    {
        Assert.Equal("False", Navigation().Attribute("AlwaysShowHeader")?.Value);
    }

    [Theory]
    [InlineData("NavigationViewContentBackground")]
    [InlineData("NavigationViewExpandedPaneBackground")]
    [InlineData("NavigationViewDefaultPaneBackground")]
    [InlineData("NavigationViewTopPaneBackground")]
    [InlineData("NavigationViewContentGridBorderBrush")]
    public void TheFiveNavigationBrushesAreTransparent(string key)
    {
        var brush = Resource(key);

        Assert.Equal(P + "SolidColorBrush", brush.Name);
        Assert.Equal("Transparent", brush.Attribute("Color")?.Value);
    }

    [Fact]
    public void TheContentBorderHasNoThickness()
    {
        var thickness = Resource("NavigationViewContentGridBorderThickness");

        Assert.Equal(P + "Thickness", thickness.Name);
        Assert.Equal("0", thickness.Value);
    }

    [Fact]
    public void TheIconBoxIsAliasedTo28()
    {
        var box = Resource("NavigationViewItemOnLeftIconBoxHeight");

        Assert.Equal(X + "Double", box.Name);
        Assert.Equal("28", box.Value);
    }

    [Fact]
    public void ThePagesSitInOneScrollViewerThatScrollsVerticallyOnly()
    {
        var doc = Load();
        var pages = doc.Descendants(P + "Grid").Single(e => e.Attribute(X + "Name")?.Value == "Pages");
        var column = pages.Parent!;
        var scroller = column.Parent!;

        Assert.Equal(P + "ScrollViewer", scroller.Name);
        Assert.Equal("Scroller", scroller.Attribute(X + "Name")?.Value);
        Assert.Equal("Disabled", scroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        // The scroll viewer is the navigation view's content, so it takes the whole content area.
        Assert.Same(doc.Descendants(P + "NavigationView").Single(), scroller.Parent);
    }

    [Fact]
    public void ThePagesFillOneStarColumnThatTheWidthCapAppliesTo()
    {
        // A page capped by its own MaxWidth would centre in a wider window; a capped star column
        // keeps it at the left edge and lets it fill up to the cap.
        var doc = Load();
        var pages = doc.Descendants(P + "Grid").Single(e => e.Attribute(X + "Name")?.Value == "Pages");
        var columns = pages.Parent!.Element(P + "Grid.ColumnDefinitions")!.Elements(P + "ColumnDefinition").ToArray();

        Assert.Single(columns);
        Assert.Equal("PageColumn", columns[0].Attribute(X + "Name")?.Value);
        Assert.Equal("*", columns[0].Attribute("Width")?.Value);
        Assert.Null(pages.Attribute("MaxWidth"));
        Assert.Null(pages.Attribute("HorizontalAlignment"));
    }

    [Fact]
    public void TheFooterIsMarkThenNameThenVersionBeneathThePane()
    {
        var footer = Named(P + "StackPanel", "Footer");

        Assert.Equal(P + "NavigationView.PaneFooter", footer.Parent!.Name);
        Assert.Equal("Horizontal", footer.Attribute("Orientation")?.Value);

        var mark = footer.Elements().First();
        Assert.Equal(P + "Image", mark.Name);
        Assert.Equal("FooterMark", mark.Attribute(X + "Name")?.Value);
        Assert.Equal("28", mark.Attribute("Width")?.Value);
        Assert.Equal("28", mark.Attribute("Height")?.Value);

        var texts = footer.Elements().Last().Elements(P + "TextBlock").ToArray();
        Assert.Equal("FooterName", texts[0].Attribute(X + "Name")?.Value);
        Assert.Equal("FooterVersion", texts[1].Attribute(X + "Name")?.Value);
        Assert.Equal("{ThemeResource TextFillColorSecondaryBrush}", texts[1].Attribute("Foreground")?.Value);
    }

    private static XDocument Load() =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "SettingsWindow.xaml"));

    private static XElement Navigation() => Load().Descendants(P + "NavigationView").Single();

    private static XElement Named(XName element, string name) =>
        Load().Descendants(element).Single(e => e.Attribute(X + "Name")?.Value == name);

    private static XElement Resource(string key) =>
        Load().Descendants(P + "Grid.Resources").Single().Elements()
            .Single(e => e.Attribute(X + "Key")?.Value == key);
}
