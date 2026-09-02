using System.Xml.Linq;
using Xunit;

namespace ZeroZero.Controls.Tests;

/// <summary>
/// The controls' markup read as data and held against what the plan says each control is: the
/// section header is the panel's sub-header and rule lifted, the row keeps its bubble to the left
/// of its field, and the prompt keeps its confirm on the right of an equal-width pair.
/// </summary>
public class MarkupTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Local = "using:ZeroZero.Controls.WinUI";

    [Fact]
    public void SectionHeaderRepeatsThePanelsSubHeaderTypography()
    {
        var setters = Setters(Load("MqttSettingsPanel.xaml"), "MqttSubHeaderStyle");
        var heading = Named(Load("SettingsSectionHeader.xaml"), P + "TextBlock", "HeadingText");

        Assert.Equal(setters["FontSize"], heading.Attribute("FontSize")?.Value);
        Assert.Equal(setters["FontWeight"], heading.Attribute("FontWeight")?.Value);
        Assert.Equal(setters["CharacterSpacing"], heading.Attribute("CharacterSpacing")?.Value);
    }

    [Fact]
    public void SectionHeaderLeavesColourAndFaceToTheHost()
    {
        // Inherited from the instance, so a host restyles by setting Foreground or FontFamily on
        // it; a value here would outrank that.
        var heading = Named(Load("SettingsSectionHeader.xaml"), P + "TextBlock", "HeadingText");

        Assert.Null(heading.Attribute("Foreground"));
        Assert.Null(heading.Attribute("FontFamily"));
    }

    [Fact]
    public void SectionHeaderRuleIsThePanelsDivider()
    {
        var setters = Setters(Load("MqttSettingsPanel.xaml"), "MqttSectionDividerStyle");
        var rule = Named(Load("SettingsSectionHeader.xaml"), P + "Border", "Divider");

        Assert.Equal(setters["Height"], rule.Attribute("Height")?.Value);
        Assert.Equal(setters["Background"], rule.Attribute("Background")?.Value);
    }

    [Fact]
    public void SectionHeaderRuleSitsAboveTheHeading()
    {
        var root = Load("SettingsSectionHeader.xaml").Root!.Element(P + "StackPanel")!;
        var children = root.Elements().ToArray();

        Assert.Equal("Divider", children[0].Attribute(X + "Name")?.Value);
        Assert.Contains(children[1].Descendants(P + "TextBlock"), t => t.Attribute(X + "Name")?.Value == "HeadingText");
    }

    [Fact]
    public void RowPutsTheBubbleBeforeTheField()
    {
        var stack = Load("SettingsRow.xaml").Descendants(P + "StackPanel").Single();
        var children = stack.Elements().ToArray();

        Assert.Equal("Horizontal", stack.Attribute("Orientation")?.Value);
        Assert.Equal(Local + "InfoIcon", children[0].Name);
        Assert.Equal("FieldPresenter", children[1].Attribute(X + "Name")?.Value);
    }

    [Fact]
    public void RowHidesItsBubbleUntilGivenText()
    {
        var bubble = Load("SettingsRow.xaml").Descendants(Local + "InfoIcon").Single();
        Assert.Equal("Collapsed", bubble.Attribute("Visibility")?.Value);
    }

    [Fact]
    public void PromptPutsConfirmOnTheRightOfTwoEqualColumns()
    {
        var doc = Load("TextPromptWindow.xaml");
        var buttons = doc.Descendants(P + "Button").ToDictionary(b => b.Attribute(X + "Name")!.Value);
        var grid = buttons["ConfirmButton"].Parent!;
        var columns = grid.Element(P + "Grid.ColumnDefinitions")!.Elements(P + "ColumnDefinition").ToArray();

        Assert.Equal(2, columns.Length);
        Assert.All(columns, c => Assert.Equal("*", c.Attribute("Width")?.Value));
        Assert.Equal("0", buttons["CancelButton"].Attribute("Grid.Column")?.Value);
        Assert.Equal("1", buttons["ConfirmButton"].Attribute("Grid.Column")?.Value);
        Assert.Same(grid, buttons["CancelButton"].Parent);
    }

    [Fact]
    public void PromptDrawsNoTitleBarOfItsOwnAndKeepsTheNoteHidden()
    {
        var doc = Load("TextPromptWindow.xaml");
        var note = Named(doc, P + "TextBlock", "NoteText");

        Assert.Equal("Collapsed", note.Attribute("Visibility")?.Value);
        Assert.NotNull(doc.Descendants(P + "MicaBackdrop").SingleOrDefault());
    }

    private static XDocument Load(string file) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, file));

    private static XElement Named(XDocument doc, XName element, string name) =>
        doc.Descendants(element).Single(e => e.Attribute(X + "Name")?.Value == name);

    private static Dictionary<string, string> Setters(XDocument doc, string styleKey) =>
        doc.Descendants(P + "Style").Single(s => s.Attribute(X + "Key")?.Value == styleKey)
            .Elements(P + "Setter")
            .ToDictionary(s => s.Attribute("Property")!.Value, s => s.Attribute("Value")!.Value);
}
