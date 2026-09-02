using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZeroZero.Controls.WinUI;

/// <summary>
/// One settings row: a header, an optional description, an optional info bubble and the field
/// that edits the setting, on a Community Toolkit card. The bubble sits to the left of the field,
/// so a row with one and a row without share the same right edge.
/// </summary>
/// <remarks>
/// <para>The toolkit card is an implementation detail: it reaches no public signature here, so a
/// host names no toolkit type and a toolkit upgrade is never an API change on this row.</para>
/// <para>Every property is a dependency property, so a row built in code from a declared group
/// binds rather than assigns. <see cref="Header"/> and <see cref="Description"/> take a string or
/// an element, the way the card does. The bubble shows only when <see cref="Info"/> holds text.
/// <see cref="FieldWidth"/> pins the field to one width, the panel's contract of one field width
/// per page; left unset the field keeps its natural size, which is what a toggle wants.</para>
/// </remarks>
public sealed partial class SettingsRow : UserControl
{
    public SettingsRow()
    {
        InitializeComponent();
    }

    /// <summary>The row's name: a string, or an element for a header that is not plain text.</summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(object), typeof(SettingsRow),
        new PropertyMetadata(null, (d, e) => ((SettingsRow)d).ApplyHeader(e.NewValue)));

    /// <summary>The line beneath the header: a string, or an element. Null shows none.</summary>
    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(object), typeof(SettingsRow),
        new PropertyMetadata(null, (d, e) => ((SettingsRow)d).Card.Description = e.NewValue));

    /// <summary>The explanation behind the bubble. Empty hides the bubble.</summary>
    public string Info
    {
        get => (string)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(
        nameof(Info), typeof(string), typeof(SettingsRow),
        new PropertyMetadata("", (d, _) => ((SettingsRow)d).ApplyInfo()));

    /// <summary>What the bubble is about, for its accessible name. A string header when left empty.</summary>
    public string InfoSubject
    {
        get => (string)GetValue(InfoSubjectProperty);
        set => SetValue(InfoSubjectProperty, value);
    }

    public static readonly DependencyProperty InfoSubjectProperty = DependencyProperty.Register(
        nameof(InfoSubject), typeof(string), typeof(SettingsRow),
        new PropertyMetadata("", (d, _) => ((SettingsRow)d).ApplyInfo()));

    /// <summary>The control that edits the setting.</summary>
    public object? Field
    {
        get => GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field), typeof(object), typeof(SettingsRow),
        new PropertyMetadata(null, (d, e) => ((SettingsRow)d).FieldPresenter.Content = e.NewValue));

    /// <summary>The width the field is held to, in device-independent units. NaN, the default,
    /// leaves the field at its natural size.</summary>
    public double FieldWidth
    {
        get => (double)GetValue(FieldWidthProperty);
        set => SetValue(FieldWidthProperty, value);
    }

    public static readonly DependencyProperty FieldWidthProperty = DependencyProperty.Register(
        nameof(FieldWidth), typeof(double), typeof(SettingsRow),
        new PropertyMetadata(double.NaN, (d, e) => ((SettingsRow)d).FieldPresenter.Width = (double)e.NewValue));

    private void ApplyHeader(object? header)
    {
        // The card's Header is projected as non-nullable, but null is how a card shows no header.
        Card.Header = header!;
        ApplyInfo();
    }

    private void ApplyInfo()
    {
        string info = Info ?? "";
        Bubble.Info = info;
        string subject = InfoSubject;
        Bubble.Subject = !string.IsNullOrEmpty(subject) ? subject : (Header as string) ?? "";
        Bubble.Visibility = info.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
