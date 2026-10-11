using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ZeroZero.Controls.WinUI;

/// <summary>
/// A single-line text prompt: title, message, one field, an optional note, cancel and confirm.
/// Frameless, on a Mica backdrop, always on top and centred on the monitor under the cursor,
/// which is where a tray application's user is looking. <see cref="ShowAsync"/> resolves with the
/// text on confirm and null on cancel, Escape or the window closing any other way.
/// </summary>
/// <remarks>
/// Enter confirms from the field; Escape cancels from any control. The confirm button waits for
/// text unless the options allow an empty answer. The window owns no wording: every string is the caller's, and
/// the theme is the caller's too, so an application pinned dark gets a dark prompt.
/// </remarks>
public sealed partial class TextPromptWindow : Window
{
    /// <summary>Client width in device-independent units.</summary>
    private const double ContentWidth = 360;

    private readonly TextPromptOptions _options;
    private readonly TaskCompletionSource<string?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Opens the prompt and resolves with the answer, or null when it was dismissed.</summary>
    public static Task<string?> ShowAsync(TextPromptOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var window = new TextPromptWindow(options);
        window.Activate();
        return window.Result;
    }

    public TextPromptWindow(TextPromptOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        InitializeComponent();

        Title = options.Title;
        Root.RequestedTheme = options.Theme;
        TitleText.Text = options.Title;
        MessageText.Text = options.Message;
        ConfirmButton.Content = options.Confirm;
        CancelButton.Content = options.Cancel;
        Field.PlaceholderText = options.Placeholder;
        Field.MaxLength = options.MaxLength;
        Field.Text = options.InitialText;
        if (options.Note is { Length: > 0 } note)
        {
            NoteText.Text = note;
            NoteText.Visibility = Visibility.Visible;
        }
        RefreshConfirm();

        ContentSizedPopup.Attach(this, Root, ContentWidth, Cancel);
        Closed += OnClosed;
        // Subscribed after Attach, so the window is fitted before the field takes focus.
        Root.Loaded += (_, _) =>
        {
            Field.SelectAll();
            Field.Focus(FocusState.Programmatic);
        };
    }

    /// <summary>The answer: the field's text on confirm, null otherwise.</summary>
    public Task<string?> Result => _completion.Task;

    private void OnTextChanged(object sender, RoutedEventArgs e) => RefreshConfirm();

    private void OnFieldKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ConfirmButton.IsEnabled)
        {
            e.Handled = true;
            Confirm();
        }
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e) => Confirm();

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Cancel();

    private void Confirm()
    {
        // Set before Close, whose handler resolves null for every other way out.
        _completion.TrySetResult(Field.Text);
        Close();
    }

    private void Cancel()
    {
        _completion.TrySetResult(null);
        Close();
    }

    // Closing while the field holds its opening selection crashes the process with an access
    // violation in the XAML runtime, so the selection is collapsed here, on every way out: Cancel,
    // Confirm, Alt+F4 and any other close. Cancel and Confirm resolve before they close; this
    // resolves null for the rest. Not AppWindow.Closing: a handler on it keeps the process alive
    // after this window, its last, closes.
    private void OnClosed(object sender, WindowEventArgs args)
    {
        Field.Select(Field.Text.Length, 0);
        _completion.TrySetResult(null);
    }

    private void RefreshConfirm() =>
        ConfirmButton.IsEnabled = _options.AllowEmpty || !string.IsNullOrWhiteSpace(Field.Text);
}
