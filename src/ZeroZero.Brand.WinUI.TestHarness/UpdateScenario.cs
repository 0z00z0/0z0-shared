using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using ZeroZero.Brand.Core;
using ZeroZero.Primitives;
using ZeroZero.Update;
using ZeroZero.Update.Win32;
using ZeroZero.Update.WinUI;
using ZeroZero.Win32;
using CoreBrand = ZeroZero.Brand.Core.Brand;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// The shared update window on screen, and the two things about it that cannot be judged from a
/// build: that the path showing nothing still shows nothing, and that a window beneath it which
/// dismisses itself on focus loss stays where it is.
/// </summary>
/// <remarks>
/// A stage run opens the window twice, once per theme, side by side on the monitor under the
/// cursor, so one capture run yields both pictures. The window shows one stage at a time by
/// design, so a picture of each stage costs a run of its own.
/// </remarks>
internal static class UpdateScenario
{
    /// <summary>The application name the rig's windows carry.</summary>
    private const string ApplicationName = "Brand Test Harness";

    private static readonly Version Running = new(1, 0, 0);

    /// <summary>A release with notes long enough to show the panel scrolling, and a hash line the
    /// stripper is expected to drop.</summary>
    internal static readonly ReleaseInfo Release = new(
        "v1.2.3", new Version(1, 2, 3, 0), "1.2.3", "Harness v1.2.3",
        """
        ## Harness v1.2.3

        **The download is now shown.** The progress a host is handed is paced for a user interface rather than emitted per network read.

        - The last report carries the download's true byte count.
        - An unknown total stays unknown, so nothing is guessed.
        - Verification runs after the last byte and reports nothing at all.

        **SHA256 (installer):** `AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084`
        """,
        new Uri("https://example.invalid/releases/tag/v1.2.3"), null,
        [new ReleaseAsset("Harness-Setup-1.2.3.exe", 48_234_496, new Uri("https://example.invalid/download/Harness-Setup-1.2.3.exe"))]);

    private static readonly List<UpdateWindow> Windows = [];
    private static readonly List<DownloadSurface> Surfaces = [];

    /// <summary>Opens the named stage in both themes, side by side. The windows are left on screen
    /// for the capture; nothing answers them.</summary>
    internal static void ShowStage(string stage)
    {
        var (workArea, scale) = MonitorMetrics.ForCursor();

        foreach (ElementTheme theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        {
            var window = new UpdateWindow(new UpdateWindowOptions
            {
                ApplicationName = ApplicationName,
                Theme = theme,
            })
            {
                // The capture script finds each window by title; the window sets the application
                // name by default, which both of these would share.
                Title = $"Update {theme} {stage}",
            };
            Windows.Add(window);
            Drive(window, stage);
        }

        // Parked once the windows have settled, not before. Each one sizes itself again when its
        // content loads and recentres on the monitor as it does, so a move made now is undone a
        // moment later and both windows end up on the same spot.
        _park = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _park.Interval = TimeSpan.FromMilliseconds(700);
        _park.IsRepeating = false;
        _park.Tick += (_, _) => Park(workArea, scale);
        _park.Start();
    }

    /// <summary>Held in a field: a local timer is unrooted and can be collected before it ticks.</summary>
    private static DispatcherQueueTimer? _park;

    private static void Drive(UpdateWindow window, string stage)
    {
        switch (stage)
        {
            case "question":
                _ = window.AskAsync(Release, Running);
                break;

            case "download":
                _ = window.AskAsync(Release, Running);
                Report(window, 18_612_224, 48_234_496);
                break;

            case "verifying":
                _ = window.AskAsync(Release, Running);
                Report(window, 48_234_496, 48_234_496);
                break;

            case "refusal":
                _ = window.ShowMessageAsync(
                    UpdateMessages.CannotInstallHeadline(Refused),
                    UpdateMessages.CannotInstallText(Refused),
                    attention: true);
                break;

            case "failure":
                _ = window.ShowMessageAsync(
                    UpdateMessages.LaunchFailedHeadline,
                    UpdateMessages.LaunchFailedText(new LaunchResult(false, "the file changed after it was verified")),
                    attention: true);
                break;

            case "uptodate":
                _ = window.ShowMessageAsync(
                    UpdateMessages.UpToDateHeadline, UpdateMessages.UpToDateText(Running), attention: false);
                break;

            case "check-failed":
                _ = window.ShowMessageAsync(
                    UpdateMessages.CheckFailedHeadline,
                    UpdateMessages.CheckFailedText(new UpdateCheckResult(
                        UpdateCheckOutcome.TimedOut, Running, Detail: "no answer within 10 s")),
                    attention: true);
                break;

            default:
                throw new ArgumentException($"No update stage named '{stage}'.", nameof(stage));
        }
    }

    /// <summary>Takes the window through the question into the download and reports one measurement
    /// into it, which is the only way the bar and its line get a value without a network.</summary>
    private static void Report(UpdateWindow window, long received, long total)
    {
        DownloadSurface surface = window.BeginDownload(Release);
        // Held: the window's reporter posts to the window's own thread, and a collected reporter
        // would never deliver.
        Surfaces.Add(surface);
        surface.Progress?.Report(new DownloadProgress(received, total));
    }

    /// <summary>An update refused by verification: the hash the release published is not the file's.</summary>
    private static PreparedUpdate Refused => new(
        PrepareOutcome.Refused, Release, "Harness-Setup-1.2.3.exe", null, null,
        new VerificationResult(VerificationVerdict.HashMismatch,
            "the file hashes to 9F2C…41B7 and the release publishes AD26…E084"),
        "refused by verification");

    /// <summary>Puts the two windows side by side about the centre of the work area, at whatever
    /// scaling the monitor has.</summary>
    private static void Park(NativeRect workArea, double scale)
    {
        int gap = (int)(24 * scale);
        int total = Windows.Sum(window => window.AppWindow.Size.Width) + gap * (Windows.Count - 1);
        int x = workArea.Left + (workArea.Right - workArea.Left - total) / 2;

        foreach (var window in Windows)
        {
            var size = window.AppWindow.Size;
            window.AppWindow.Move(new PointInt32(
                x, workArea.Top + Math.Max(0, (workArea.Bottom - workArea.Top - size.Height) / 2)));
            x += size.Width + gap;
        }
    }

    /// <summary>
    /// The silent trigger over a service that has a release to offer, with the real window prompts
    /// wired up. Writes what the run answered; what it put on screen is counted from outside, by
    /// the script that runs this.
    /// </summary>
    internal static async Task RunSilentAsync(string probePath, Action exit)
    {
        var prompts = new UpdateWindowPrompts(new UpdateWindowOptions { ApplicationName = ApplicationName });
        var flow = new UpdateFlow(new OfferingService(), prompts, new UpdateFlowOptions
        {
            Shutdown = () => { },
            Log = NullLogSink.Instance,
        });

        UpdateFlowRun run = await flow.RunAsync(UpdateTrigger.Silent);

        await File.WriteAllTextAsync(probePath,
            $"result\t{run.Result}{Environment.NewLine}release\t{run.Release?.TagName ?? "<none>"}{Environment.NewLine}");
        await File.WriteAllTextAsync(probePath + ".done", "");

        // Long enough for the script to enumerate this process's windows, and no longer.
        await Task.Delay(TimeSpan.FromSeconds(6));
        exit();
    }

    /// <summary>
    /// A window that dismisses itself on focus loss, with another opened on top of it. Under
    /// <c>update</c> the window on top is the update window, which counts itself among the
    /// application's transient windows; under <c>plain</c> it is an ordinary window that does not,
    /// which is the control — without it, a run where the About window survives would say nothing
    /// about the count doing the work.
    /// </summary>
    internal static async Task RunOverAboutAsync(string mode, string logPath, Action exit)
    {
        var about = new BrandAboutWindow(new BrandAboutOptions
        {
            Info = new AboutInfo
            {
                AppName = ApplicationName,
                Version = "0.0.0-dev",
                Description = "A window that dismisses itself the moment it loses focus.",
                RepoUrl = $"{CoreBrand.OrgUrl}/0z0-shared",
            },
        })
        {
            Title = "About Beneath",
        };

        bool aboutClosed = false;
        about.Closed += (_, _) => aboutClosed = true;
        about.Activate();

        // The About window ignores a deactivation arriving before its first activation, so the
        // window on top must not open until that latch has been set.
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Window onTop = mode == "plain" ? PlainWindow() : QuestionWindow();
        onTop.Activate();

        await Task.Delay(TimeSpan.FromSeconds(2));

        await File.WriteAllTextAsync(logPath,
            $"mode\t{mode}{Environment.NewLine}about-closed\t{aboutClosed}{Environment.NewLine}");
        await File.WriteAllTextAsync(logPath + ".done", "");

        await Task.Delay(TimeSpan.FromSeconds(3));
        exit();
    }

    private static Window QuestionWindow()
    {
        var window = new UpdateWindow(new UpdateWindowOptions
        {
            ApplicationName = ApplicationName,
            Theme = ElementTheme.Dark,
        })
        {
            Title = "Update On Top",
        };
        _ = window.AskAsync(Release, Running);
        return window;
    }

    /// <summary>The control: the same shape of window, counted among nothing.</summary>
    private static Window PlainWindow()
    {
        var (workArea, scale) = MonitorMetrics.ForCursor();
        var window = new Window
        {
            Title = "Plain On Top",
            Content = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x20, 0x20)) },
        };
        window.AppWindow.MoveAndResize(new RectInt32(
            workArea.Left + 200, workArea.Top + 200, (int)(380 * scale), (int)(220 * scale)));
        return window;
    }

    /// <summary>A service with a release newer than the running version, reaching no network.</summary>
    private sealed class OfferingService : IUpdateService
    {
        public Version RunningVersion => Running;

        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Running, Release));

        public Task<PreparedUpdate> PrepareAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A silent run must never reach the install path.");

        public LaunchResult Launch(PreparedUpdate update) =>
            throw new InvalidOperationException("A silent run must never reach the launch path.");

        public int SweepStaleDownloads(TimeSpan olderThan) => 0;
    }
}
