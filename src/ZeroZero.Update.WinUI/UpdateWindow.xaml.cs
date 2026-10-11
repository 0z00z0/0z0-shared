using System.Diagnostics;
using Microsoft.UI.Xaml;
using ZeroZero.Brand.WinUI;
using ZeroZero.Controls.WinUI;
using ZeroZero.Update.Win32;
using ZeroZero.Win32;

namespace ZeroZero.Update.WinUI;

/// <summary>
/// Every step a person sees during an update, in one window that changes what it shows: the
/// question with its release notes, the download with its progress, and a message with one way out
/// — a refusal, a failure, or the notice that nothing newer exists.
/// </summary>
/// <remarks>
/// One window, so "Install now" starts the download where the question was. It owns no wording:
/// every sentence comes from <see cref="UpdateMessages"/>. docs/zerozero-update.md describes the window.
/// </remarks>
public sealed partial class UpdateWindow : Window
{
    /// <summary>Client width in device-independent units.</summary>
    private const double ContentWidth = 380;

    private enum Stage
    {
        Question,
        Downloading,
        Message,
    }

    private readonly UpdateWindowOptions _options;
    private readonly IDisposable _transient;
    private readonly ContentSizedPopup _popup;

    private TaskCompletionSource<InstallChoice>? _answer;
    private TaskCompletionSource<bool>? _read;

    /// <summary>Trips when the person stops the download. One per download, so a second run in the
    /// same window is not born already cancelled.</summary>
    private CancellationTokenSource? _stopping;

    private Stage _stage = Stage.Question;
    private ReleaseInfo? _downloading;
    private bool _hasDetail;
    private bool _hasReleasePage;
    private bool _shown;
    private bool _closed;

    public UpdateWindow(UpdateWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        InitializeComponent();

        Title = options.ApplicationName;
        Root.RequestedTheme = options.Theme;
        AppNameText.Text = options.ApplicationName;

        InstallButton.Label = UpdateMessages.InstallLabel;
        LaterButton.Label = UpdateMessages.LaterLabel;
        NotesButton.Label = UpdateMessages.ReleasePageLabel;
        CloseButton.Label = UpdateMessages.CloseLabel;
        CancelButton.Label = UpdateMessages.CancelDownloadLabel;

        InstallButton.Click += (_, _) => Answer(InstallChoice.Install);
        LaterButton.Click += (_, _) => Answer(InstallChoice.Later);
        NotesButton.Click += (_, _) => Answer(InstallChoice.OpenReleasePage);
        CloseButton.Click += (_, _) => Finish();
        CancelButton.Click += (_, _) => StopDownload();
        DismissButton.Click += (_, _) => Escape();

        _transient = TransientWindows.Enter();
        Closed += OnClosed;

        // Escape is the same act as pressing the way out this stage offers, so it takes the same path.
        _popup = ContentSizedPopup.Attach(this, Root, ContentWidth, Escape);
    }

    /// <summary>
    /// Asks whether to install and completes when the person has chosen. Closing the window any
    /// other way answers <see cref="InstallChoice.Later"/>: the cross on a question means not now.
    /// </summary>
    public Task<InstallChoice> AskAsync(ReleaseInfo release, Version runningVersion)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(runningVersion);

        _answer = new TaskCompletionSource<InstallChoice>(TaskCreationOptions.RunContinuationsAsynchronously);

        HeadlineText.Text = UpdateMessages.AvailableHeadline(release);
        BodyText.Text = UpdateMessages.InstallBody(runningVersion, _options.ApplicationName);

        // The host's text is taken as written; only the release body is stripped of its markdown.
        string notes = _options.ReleaseNotes?.Invoke(release) ?? ReleaseNotesText.Strip(release.Body);
        DetailText.Text = notes;
        _hasDetail = notes.Length > 0;
        _hasReleasePage = release.HtmlUri is not null;

        GoTo(Stage.Question);
        return _answer.Task;
    }

    /// <summary>
    /// Turns the window over to the download and returns where its progress goes. The reporter
    /// marshals to this window's own thread, so the flow may hand it to a download running
    /// anywhere.
    /// </summary>
    public DownloadSurface BeginDownload(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);

        _stopping?.Dispose();
        _stopping = new CancellationTokenSource();
        _downloading = release;
        HeadlineText.Text = UpdateMessages.DownloadingHeadline(release);
        BodyText.Text = UpdateMessages.DownloadBody(_options.ApplicationName);
        DownloadBar.IsIndeterminate = true;
        DownloadBar.Value = 0;
        ProgressText.Text = UpdateMessages.DownloadProgressText(0, null);

        GoTo(Stage.Downloading);
        return new DownloadSurface(new MarshalledProgress(this), _stopping.Token);
    }

    /// <summary>
    /// Puts one message in the window with a single way out, and completes when it has been taken.
    /// <paramref name="attention"/> is a refusal or a failure rather than a plain notice, which the
    /// action button shows in the brand's attention colours.
    /// </summary>
    public Task ShowMessageAsync(string headline, string body, bool attention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headline);

        _read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        HeadlineText.Text = headline;
        BodyText.Text = body ?? "";
        CloseButton.State = attention ? BrandBracketButtonState.Attention : BrandBracketButtonState.Rest;

        GoTo(Stage.Message);
        return _read.Task;
    }

    /// <summary>Takes the window off the screen, answering whatever is still pending.</summary>
    public void Dismiss()
    {
        if (_closed) return;
        _closed = true;
        Close();
    }

    /// <summary>What the cross and Escape do, which is whatever the stage's own way out is. During
    /// a download that is stopping it, not hiding the window from it.</summary>
    private void Escape()
    {
        if (_stage == Stage.Downloading) StopDownload();
        else Dismiss();
    }

    /// <summary>Stops the download and goes. The downloader removes the partial file and the
    /// directory it was going into, and the flow answers the run without reporting anything: the
    /// person who stopped it knows what happened.</summary>
    private void StopDownload()
    {
        try { _stopping?.Cancel(); }
        catch (ObjectDisposedException) { /* already gone; the window is closing anyway */ }
        Dismiss();
    }

    private void Answer(InstallChoice choice)
    {
        // Set before the window goes: the Closed handler answers Later for every other way out.
        _answer?.TrySetResult(choice);
        _answer = null;

        // Installing keeps the window: the download runs in it. Everything else ends the run.
        if (choice != InstallChoice.Install) Dismiss();
    }

    private void Finish()
    {
        _read?.TrySetResult(true);
        _read = null;
        Dismiss();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _transient.Dispose();
        _answer?.TrySetResult(InstallChoice.Later);
        _read?.TrySetResult(true);
        // A window closed while a download runs stops it: nothing is left downloading behind a
        // window that is no longer there to report it.
        try { _stopping?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Moves to a stage: what that stage shows, the window on screen, and a size that
    /// fits the content the stage put in it.</summary>
    private void GoTo(Stage stage)
    {
        _stage = stage;
        bool question = stage == Stage.Question;
        bool downloading = stage == Stage.Downloading;
        bool message = stage == Stage.Message;

        InstallButton.Visibility = Show(question);
        LaterButton.Visibility = Show(question);
        NotesButton.Visibility = Show(question && _hasReleasePage);
        CloseButton.Visibility = Show(message);
        CancelButton.Visibility = Show(downloading);
        ProgressPanel.Visibility = Show(downloading);
        DetailCard.Visibility = Show(question && _hasDetail);
        BodyText.Visibility = Show(BodyText.Text.Length > 0);

        // The download has a button of its own that stops it; a second cross beside it would be a
        // way out meaning something different.
        DismissButton.Visibility = Show(!downloading);

        if (!_shown)
        {
            _shown = true;
            Activate();
        }

        _popup.Fit();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What a report does to the bar and the line under it. On the window's own thread.</summary>
    private void ApplyProgress(DownloadProgress progress)
    {
        if (_closed || _stage != Stage.Downloading) return;

        try
        {
            if (progress.Fraction is { } fraction)
            {
                DownloadBar.IsIndeterminate = false;
                DownloadBar.Value = fraction;
            }
            else
            {
                DownloadBar.IsIndeterminate = true;
            }

            // Verification runs after the last byte and reports nothing, so a bar sitting full with
            // the byte count still under it reads as a download that stalled. The headline moves
            // with the line: nothing is downloading any more.
            bool whole = progress.TotalBytes is { } total && total > 0 && progress.BytesReceived >= total;
            ProgressText.Text = whole
                ? UpdateMessages.VerifyingText
                : UpdateMessages.DownloadProgressText(progress.BytesReceived, progress.TotalBytes);
            if (_downloading is { } release)
                HeadlineText.Text = whole
                    ? UpdateMessages.VerifyingHeadline(release)
                    : UpdateMessages.DownloadingHeadline(release);
        }
        catch (Exception ex)
        {
            // A report can land between the window closing and the guard above seeing it, and
            // touching a closed window raises RO_E_CLOSED. A missed report is not worth a crash.
            Debug.WriteLine($"UpdateWindow: reporting progress: {ex}");
        }
    }

    /// <summary>A reporter that hands every report to the window's own thread. The download runs on
    /// whatever thread it was started from, and a window is touched from one.</summary>
    private sealed class MarshalledProgress(UpdateWindow window) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) =>
            window.DispatcherQueue.TryEnqueue(() => window.ApplyProgress(value));
    }
}
