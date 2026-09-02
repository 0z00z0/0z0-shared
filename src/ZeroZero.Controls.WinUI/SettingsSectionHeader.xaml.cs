using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZeroZero.Controls.WinUI;

/// <summary>
/// The sub-heading that opens a group of settings rows: a rule, the heading, an optional info
/// bubble and an optional trailing marker. The rule is above the heading and carries the gap
/// between groups, so the heading reads as belonging to the cards beneath it.
/// </summary>
/// <remarks>
/// Every property is a dependency property, so a page built in code binds rather than assigns.
/// The bubble shows only when <see cref="Info"/> holds text: an icon that opens on an empty
/// flyout is worse than no icon. Colour and face are inherited — a host sets Foreground or
/// FontFamily on the instance — and the size, weight and spacing are the heading's own.
/// </remarks>
public sealed partial class SettingsSectionHeader : UserControl
{
    public SettingsSectionHeader()
    {
        InitializeComponent();
    }

    /// <summary>The heading text.</summary>
    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(SettingsSectionHeader),
        new PropertyMetadata("", (d, _) => ((SettingsSectionHeader)d).ApplyHeading()));

    /// <summary>The explanation behind the bubble. Empty hides the bubble.</summary>
    public string Info
    {
        get => (string)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(
        nameof(Info), typeof(string), typeof(SettingsSectionHeader),
        new PropertyMetadata("", (d, _) => ((SettingsSectionHeader)d).ApplyInfo()));

    /// <summary>What the bubble is about, for its accessible name. The heading when left empty.</summary>
    public string InfoSubject
    {
        get => (string)GetValue(InfoSubjectProperty);
        set => SetValue(InfoSubjectProperty, value);
    }

    public static readonly DependencyProperty InfoSubjectProperty = DependencyProperty.Register(
        nameof(InfoSubject), typeof(string), typeof(SettingsSectionHeader),
        new PropertyMetadata("", (d, _) => ((SettingsSectionHeader)d).ApplyInfo()));

    /// <summary>Whether the rule above the heading is drawn. Off for the first group on a page,
    /// which has nothing above it to be separated from.</summary>
    public bool ShowDivider
    {
        get => (bool)GetValue(ShowDividerProperty);
        set => SetValue(ShowDividerProperty, value);
    }

    public static readonly DependencyProperty ShowDividerProperty = DependencyProperty.Register(
        nameof(ShowDivider), typeof(bool), typeof(SettingsSectionHeader),
        new PropertyMetadata(true, (d, e) => ((SettingsSectionHeader)d).Divider.Visibility =
            (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    /// <summary>Anything shown after the bubble on the heading line: a staged-edit marker, a
    /// count. Outside any collapsible group, so a state only a closed group could show is still on
    /// screen.</summary>
    public object? Trailing
    {
        get => GetValue(TrailingProperty);
        set => SetValue(TrailingProperty, value);
    }

    public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
        nameof(Trailing), typeof(object), typeof(SettingsSectionHeader),
        new PropertyMetadata(null, (d, e) => ((SettingsSectionHeader)d).TrailingPresenter.Content = e.NewValue));

    private void ApplyHeading()
    {
        HeadingText.Text = Heading ?? "";
        ApplyInfo();
    }

    private void ApplyInfo()
    {
        string info = Info ?? "";
        Bubble.Info = info;
        Bubble.Subject = string.IsNullOrEmpty(InfoSubject) ? Heading ?? "" : InfoSubject;
        Bubble.Visibility = info.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
