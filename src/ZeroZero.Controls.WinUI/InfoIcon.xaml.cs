using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ZeroZero.Controls.WinUI;

/// <summary>
/// A small "(i)" button that opens its explanation in a flyout. The one place a settings row puts
/// the how-it-works detail that would otherwise sit in the visible copy: a card header and its
/// description say what a control does, and everything longer moves in here.
/// </summary>
/// <remarks>
/// Every property is a dependency property. A general-purpose control cannot assume its call sites
/// are literal strings in markup — <c>{x:Bind}</c> targets and <c>Style</c> setters each require a
/// DP, and a row built in code from a declared group binds rather than assigns.
/// </remarks>
public sealed partial class InfoIcon : UserControl
{
    /// <summary>The Fluent "info" glyph, and what an empty <see cref="GlyphCode"/> falls back to: a
    /// blank icon button is indistinguishable from a rendering fault.</summary>
    private const string DefaultGlyph = "\uE946";

    public InfoIcon()
    {
        InitializeComponent();
        ApplyAutomationName();
    }

    /// <summary>The explanation shown in the flyout.</summary>
    public string Info
    {
        get => (string)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(
        nameof(Info), typeof(string), typeof(InfoIcon),
        new PropertyMetadata("", (d, e) => ((InfoIcon)d).InfoText.Text = (string?)e.NewValue ?? ""));

    /// <summary>
    /// What the explanation is about, e.g. "the broker host". Screen readers meet several of these
    /// on one page, so the accessible name has to name the setting rather than repeat "more
    /// information".
    /// </summary>
    public string Subject
    {
        get => (string)GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    public static readonly DependencyProperty SubjectProperty = DependencyProperty.Register(
        nameof(Subject), typeof(string), typeof(InfoIcon),
        new PropertyMetadata("", (d, _) => ((InfoIcon)d).ApplyAutomationName()));

    /// <summary>The glyph on the button, for a host whose icon set names it differently.</summary>
    public string GlyphCode
    {
        get => (string)GetValue(GlyphCodeProperty);
        set => SetValue(GlyphCodeProperty, value);
    }

    public static readonly DependencyProperty GlyphCodeProperty = DependencyProperty.Register(
        nameof(GlyphCode), typeof(string), typeof(InfoIcon),
        new PropertyMetadata(DefaultGlyph, (d, e) => ((InfoIcon)d).Glyph.Glyph =
            (string?)e.NewValue is { Length: > 0 } glyph ? glyph : DefaultGlyph));

    // The name is composed here rather than at each call site, so one control decides how an info
    // button introduces itself whatever a row calls its subject.
    private void ApplyAutomationName() =>
        AutomationProperties.SetName(Root,
            string.IsNullOrEmpty(Subject) ? "More information" : $"More information about {Subject}");
}
