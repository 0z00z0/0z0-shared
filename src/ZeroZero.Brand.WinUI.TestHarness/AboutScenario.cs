using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroZero.Win32;
using static ZeroZero.Brand.WinUI.TestHarness.HarnessArguments;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>The About content on screen: the popup, the hosted control, or the opener window. One
/// surface per run, because the popup closes itself on focus loss. Flags: docs/test-harness.md.</summary>
internal sealed class AboutScenario(Action showAnchor)
{
    private Window? _aboutWindow;
    private Window? _hostedControlWindow;
    private Window? _openerWindow;
    private DispatcherQueueTimer? _settledTimer;
    private DispatcherQueueTimer? _notesTimer;

    public void Show(string[] commandLine)
    {
        // Opened first, so the About window is the one that ends up with focus: the popup dismisses
        // itself the moment it loses focus, and a window activated after it would take it away.
        if (Has(commandLine, "--anchor")) showAnchor();

        // The opener owns the run: the About popup is opened by a click on its button, not at
        // launch, which is the only way the double-click gesture can be reproduced.
        if (Has(commandLine, "--opener"))
        {
            ShowOpener(commandLine);
            return;
        }

        string notesUrl = ValueAfter(commandLine, "--notes") ?? SampleInfo.DefaultNotesUrl;
        string? probePath = ValueAfter(commandLine, "--probe");
        var theme = ValueAfter(commandLine, "--theme") switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (Has(commandLine, "--hosted"))
        {
            var hostedInfo = SampleInfo.Create(
                DescriptionFrom(commandLine)
                    ?? "Same BrandAboutControl content as the popup, hosted directly inside a plain " +
                       "window with ordinary chrome and no update button — simulating an " +
                       "in-navigation About page, which has no popup or update/exit concept.",
                appName: SampleInfo.AppName + " (hosted control)",
                releaseNotesUrl: notesUrl, withLibraries: true);
            _hostedControlWindow = new HostedControlWindow(hostedInfo);
            ApplyTheme(_hostedControlWindow, theme);
            _hostedControlWindow.Activate();
            ScriptAbout(_hostedControlWindow, commandLine, probePath);
            return;
        }

        var options = new BrandAboutOptions
        {
            Info = SampleInfo.Create(
                DescriptionFrom(commandLine)
                    ?? "Interactive launch-test rig for the shared BrandAboutWindow component — " +
                       "renders the About box from this repo's own sample data, independently " +
                       "of any consuming app.",
                releaseNotesUrl: notesUrl, withLibraries: true),
            // Present so the "Check for Updates" button shows; false keeps the window open.
            OnCheckForUpdates = async () => { await Task.Delay(500); return false; },
        };

        _aboutWindow = new BrandAboutWindow(options);
        // A recognisable title so the capture script finds the window even though BrandAboutWindow
        // hides its own title bar (the AppWindow title is still set).
        _aboutWindow.Title = "Window Mode";
        ApplyTheme(_aboutWindow, theme);
        _aboutWindow.Activate();
        ScriptAbout(_aboutWindow, commandLine, probePath);
    }

    /// <summary>A window with one button that opens the popup, the only way a fast double-click on
    /// what opens it can be reproduced.</summary>
    private void ShowOpener(string[] commandLine)
    {
        var (workArea, scale) = MonitorMetrics.ForCursor();

        var button = new Button
        {
            Name = "OpenAboutBtn",
            Content = "About",
            Margin = new Thickness(24),
            Padding = new Thickness(24, 12, 24, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _openerWindow = new Window
        {
            Title = "About Opener",
            Content = new Grid { Children = { button } },
        };

        button.Click += (_, _) =>
        {
            var options = new BrandAboutOptions
            {
                Info = SampleInfo.Create(
                    "Opened from a button, so the dismissal can be driven the way a " +
                    "consuming application opens it.",
                    releaseNotesUrl: ValueAfter(commandLine, "--notes") ?? SampleInfo.DefaultNotesUrl,
                    withLibraries: true),
            };
            // Every activation change against the milliseconds since the button was pressed. The
            // order of "took focus" and "lost focus" around a double-click is the whole question,
            // and it is not something reading the handler can answer.
            string? log = ValueAfter(commandLine, "--probe");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var about = new BrandAboutWindow(options) { Title = "Window Mode" };
            if (log is { Length: > 0 })
            {
                about.Activated += (_, e) =>
                {
                    try { File.AppendAllText(log, $"{clock.ElapsedMilliseconds}\t{e.WindowActivationState}" + Environment.NewLine); }
                    catch (IOException) { /* a run that writes nothing is still a run */ }
                };
                about.Closed += (_, _) =>
                {
                    try { File.AppendAllText(log, $"{clock.ElapsedMilliseconds}\tClosed" + Environment.NewLine); }
                    catch (IOException) { }
                };
                try { File.AppendAllText(log, $"{clock.ElapsedMilliseconds}\tConstructed" + Environment.NewLine); } catch (IOException) { }
            }
            about.Activate();
        };

        _openerWindow.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            workArea.Left + 120, workArea.Top + 120, (int)(320 * scale), (int)(200 * scale)));
        _openerWindow.Activate();
    }

    /// <summary>The description a run asked for: a word on the command line, or a file where the
    /// text has spaces in it and a command line would break it into tokens.</summary>
    private static string? DescriptionFrom(string[] commandLine)
    {
        if (ValueAfter(commandLine, "--description-file") is { Length: > 0 } path && File.Exists(path))
            return File.ReadAllText(path);

        return ValueAfter(commandLine, "--description");
    }

    private static void ApplyTheme(Window window, ElementTheme theme)
    {
        if (theme != ElementTheme.Default && window.Content is FrameworkElement content)
            content.RequestedTheme = theme;
    }

    /// <summary>Presses what the run asked for once the window has settled, then writes the probe:
    /// a press changes the height the probe records, so the probe comes after it.</summary>
    private void ScriptAbout(Window window, string[] commandLine, string? probePath)
    {
        bool expand = Has(commandLine, "--expand");
        bool news = Has(commandLine, "--news");
        if (!expand && !news && probePath is not { Length: > 0 }) return;

        // The window's size when it first appears, before anything has been pressed: the sizing runs
        // twice, and the difference between the two passes is only visible this early.
        if (probePath is { Length: > 0 })
        {
            ProbeFile.WriteAfter(TimeSpan.FromMilliseconds(250), probePath + ".early.txt",
                                 path => AboutProbe.Dump(path, "early", window));
        }

        _settledTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _settledTimer.Interval = TimeSpan.FromSeconds(1.0);
        _settledTimer.IsRepeating = false;
        _settledTimer.Tick += (_, _) =>
        {
            var root = (FrameworkElement)window.Content;
            if (news && HarnessTree.FindNamed<Button>(root, "NewsBtn") is { } newsButton)
            {
                if (probePath is { Length: > 0 }) WatchNotes(root, probePath + ".timing.txt");
                HarnessTree.Press(newsButton);
            }
            if (expand && HarnessTree.FindNamed<Button>(root, "LibrariesToggleBtn") is { } toggle) HarnessTree.Press(toggle);

            if (probePath is not { Length: > 0 }) return;

            // After the presses, and after whatever they set off has had time to answer: the notes
            // fetch gives up after three seconds, so the numbers are read once that is settled too.
            ProbeFile.WriteAfter(TimeSpan.FromSeconds(news ? 4.0 : 0.5), probePath,
                                 path => AboutProbe.Dump(path, window.Title, window));
        };
        _settledTimer.Start();
    }

    /// <summary>Records every change to the notes text against the milliseconds since the press.
    /// Polled, because the control raises no event for its own text.</summary>
    private void WatchNotes(FrameworkElement root, string path)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        string last = "";
        File.WriteAllText(path, "");

        _notesTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _notesTimer.Interval = TimeSpan.FromMilliseconds(50);
        _notesTimer.IsRepeating = true;
        _notesTimer.Tick += (timer, _) =>
        {
            string now = HarnessTree.FindNamed<TextBlock>(root, "NewsText")?.Text ?? "";
            if (now != last)
            {
                last = now;
                File.AppendAllText(path, $"{started.ElapsedMilliseconds}\t{now.Replace('\n', ' ').Replace('\r', ' ')}\n");
            }
            if (started.Elapsed > TimeSpan.FromSeconds(15)) timer.Stop();
        };
        _notesTimer.Start();
    }
}
