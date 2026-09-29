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
        Closed += OnClosed;
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
        // Measured from the layout the window has, and rounded up, because a client area a pixel
        // short of its content clips the last row.
        Root.InvalidateMeasure();
        Root.UpdateLayout();
        Root.Measure(new Size(ContentWidth, double.PositiveInfinity));
        int width = (int)Math.Ceiling(ContentWidth * _scale);
        int height = (int)Math.Ceiling((Root.DesiredSize.Height > 0 ? Root.DesiredSize.Height : 200) * _scale);

        // The frame this presenter actually has, read from the window, so the client fills with
        // the content exactly at any scaling. Nothing in the prompt scrolls, so the outer height
        // stops at the work area and an over-tall prompt sits against its top.
        var (ncWidth, ncHeight) = MonitorMetrics.NonClientSize(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        int outerHeight = height + ncHeight;
        if (_workArea.Height > 0 && outerHeight > _workArea.Height) outerHeight = _workArea.Height;
        AppWindow.Resize(new SizeInt32(width + ncWidth, outerHeight));
        var outer = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            _workArea.Left + (_workArea.Width - outer.Width) / 2,
            _workArea.Top + Math.Max(0, (_workArea.Height - outer.Height) / 2)));
    }
}
