using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using ZeroZero.Win32;

namespace ZeroZero.Controls.WinUI;

/// <summary>
/// A single-line text prompt: title, message, one field, an optional note, cancel and confirm.
/// Frameless, on a Mica backdrop, always on top and centred on the monitor under the cursor,
/// which is where a tray application's user is looking. <see cref="ShowAsync"/> resolves with the
/// text on confirm and null on cancel, Escape or the window closing any other way.
/// </summary>
/// <remarks>
/// Enter confirms and Escape cancels from the field. The confirm button waits for text unless the
/// options allow an empty answer. The window owns no wording: every string is the caller's, and
/// the theme is the caller's too, so an application pinned dark gets a dark prompt.
/// </remarks>
public sealed partial class TextPromptWindow : Window
{
    /// <summary>Client width in device-independent units; the content is measured and laid out
    /// at this width, so the measured height is the height that renders.</summary>
    private const double ContentWidth = 360;

    private readonly TextPromptOptions _options;
    private readonly TaskCompletionSource<string?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NativeRect _workArea;
    private double _scale;

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

        ConfigureChrome();
        // Resolved before the window is torn down, not after: a continuation queued from Closed
        // ran into the dispatcher's own shutdown when the prompt was the last window, and the
        // process died with an access violation (measured through the harness). Closing fires
        // first for every way out, and Closed stays as the fallback.
        AppWindow.Closing += (_, _) => _completion.TrySetResult(null);
        Closed += (_, _) => _completion.TrySetResult(null);
        Root.Loaded += (_, _) =>
        {
            ResizeToContent();
            Field.SelectAll();
            Field.Focus(FocusState.Programmatic);
        };
    }

    /// <summary>The answer: the field's text on confirm, null otherwise.</summary>
    public Task<string?> Result => _completion.Task;

    private void OnTextChanged(object sender, RoutedEventArgs e) => RefreshConfirm();

    private void OnFieldKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter when ConfirmButton.IsEnabled:
                e.Handled = true;
                Confirm();
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                Cancel();
                break;
        }
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e) => Confirm();

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Cancel();

    private void Confirm()
    {
        // Set before Close, whose handlers resolve null for every other way out.
        _completion.TrySetResult(Field.Text);
        Close();
    }

    private void Cancel()
    {
        _completion.TrySetResult(null);
        Close();
    }

    private void RefreshConfirm() =>
        ConfirmButton.IsEnabled = _options.AllowEmpty || !string.IsNullOrWhiteSpace(Field.Text);

    private void ConfigureChrome()
    {
        AppWindow.IsShownInSwitchers = false;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);

        Root.Width = ContentWidth;
        (_workArea, _scale) = MonitorMetrics.ForCursor();
        // Provisional: text measured before the tree is live uses fallback metrics. Loaded
        // measures again with the layout on screen.
        ResizeToContent();
    }

    private void ResizeToContent()
    {
        Root.Measure(new Size(ContentWidth, double.PositiveInfinity));
        int width = (int)Math.Round(ContentWidth * _scale);
        int height = (int)Math.Round((Root.DesiredSize.Height > 0 ? Root.DesiredSize.Height : 200) * _scale);

        // The frame this presenter actually has, read from the window, so the client fills with
        // the content exactly at any scaling; centred with the outer size that results.
        var (ncWidth, ncHeight) = MonitorMetrics.NonClientSize(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        AppWindow.Resize(new SizeInt32(width + ncWidth, height + ncHeight));
        var outer = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            _workArea.Left + (_workArea.Width - outer.Width) / 2,
            _workArea.Top + (_workArea.Height - outer.Height) / 2));
    }
}
