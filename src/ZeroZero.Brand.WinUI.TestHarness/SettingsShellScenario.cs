using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using ZeroZero.Brand.Core;
using ZeroZero.Controls.WinUI;
using ZeroZero.Mqtt.WinUI;
using ZeroZero.SettingsShell.WinUI;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// The settings window shell on screen with four fabricated sections: a page built from the row
/// vocabulary, the MQTT settings panel as a build-once section, a page whose timer runs only
/// while it is on screen, and the About control hosted in navigation. Every hook, build, load
/// and save writes a line to <c>settings-shell-log.txt</c> in the temp folder, so the order the
/// shell calls them in is measured rather than read off the source; the rig's own steps —
/// navigating, maximising, closing — are scripted from the command line so a run can be driven
/// without a hand on the mouse.
/// </summary>
internal static class SettingsShellScenario
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "settings-shell-log.txt");

    private static readonly Uri MarkUri = new("ms-appx:///Assets/harness-mark.svg");

    internal sealed class Options
    {
        public bool Fit { get; init; }
        public WindowRect? SeedRect { get; init; }
        public string[] NavigateTo { get; init; } = [];
        public bool Maximise { get; init; }
        public int CloseAfterMs { get; init; }
        public bool Rebuild { get; init; }
    }

    /// <summary>The store an application keeps in its own document, here in memory and logged, so
    /// what the shell loaded and what it saved are both on record.</summary>
    private sealed class MemoryRectStore(string name, WindowRect? seed) : IWindowRectStore
    {
        private WindowRect? _rect = seed;

        public WindowRect? Load()
        {
            Log($"{name}: load {(_rect is { } r ? Describe(r) : "none")}");
            return _rect;
        }

        public void Save(WindowRect rect)
        {
            _rect = rect;
            Log($"{name}: save {Describe(rect)}");
        }
    }

    public static SettingsWindow Open(string title, ElementTheme theme, Options options)
    {
        int generalBuilds = 0;
        MqttSettingsPanel? panel = null;
        DispatcherTimer? timer = null;
        SettingsWindow? window = null;

        SettingsSection[] sections =
        [
            new()
            {
                Tag = "general",
                Label = "General",
                Icon = new FontIconSource { Glyph = "" },
                Build = () =>
                {
                    generalBuilds++;
                    Log($"{title}: build general #{generalBuilds}");
                    return BuildGeneral(generalBuilds, () => window!);
                },
                Enter = () => Log($"{title}: enter general"),
                Leave = () => Log($"{title}: leave general"),
            },
            new()
            {
                Tag = "homeassistant",
                Label = "Home Assistant",
                Icon = new ImageIconSource { ImageSource = SvgIcon(title, "homeassistant", rasterise: true) },
                BuildOnce = true,
                Build = () =>
                {
                    Log($"{title}: build homeassistant");
                    // The panel exactly as its adoption document has a host embed it: the heading
                    // is the host's, the panel scrolls nothing itself, Initialise is called once.
                    panel = new MqttSettingsPanel();
                    panel.Initialise(MqttPanelSample.Build());
                    return new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock { Text = "MQTT", FontSize = 20, FontWeight = FontWeights.SemiBold },
                            panel,
                        },
                    };
                },
                Enter = () =>
                {
                    panel?.Refresh();
                    Log($"{title}: enter homeassistant");
                },
                Leave = () => Log($"{title}: leave homeassistant"),
            },
            new()
            {
                Tag = "timer",
                Label = "Timer",
                Icon = new ImageIconSource { ImageSource = SvgIcon(title, "timer", rasterise: false) },
                Build = () =>
                {
                    Log($"{title}: build timer");
                    var ticks = new TextBlock { Text = "0 ticks", FontSize = 28 };
                    int count = 0;
                    timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    timer.Tick += (_, _) =>
                    {
                        count++;
                        ticks.Text = $"{count} ticks";
                        Log($"{title}: tick {count}");
                    };
                    return new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "Timer", FontSize = 20, FontWeight = FontWeights.SemiBold },
                            new TextBlock
                            {
                                Text = "The counter runs only while this page is on screen: the enter hook starts it and the leave hook stops it.",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            ticks,
                        },
                    };
                },
                Enter = () =>
                {
                    timer?.Start();
                    Log($"{title}: enter timer");
                },
                Leave = () =>
                {
                    timer?.Stop();
                    Log($"{title}: leave timer");
                },
            },
            new()
            {
                Tag = "about",
                Label = "About",
                Icon = new FontIconSource { Glyph = "" },
                Build = () =>
                {
                    Log($"{title}: build about");
                    var about = new BrandAboutControl();
                    about.SetInfo(new AboutInfo
                    {
                        AppName = "Brand Test Harness",
                        Version = "0.0.0-dev",
                        Description = "The About control hosted in the settings window's navigation, the way a full windowed application hosts it.",
                        RepoUrl = $"{Core.Brand.OrgUrl}/0z0-shared",
                    });
                    return about;
                },
                Enter = () => Log($"{title}: enter about"),
                Leave = () => Log($"{title}: leave about"),
            },
        ];

        window = new SettingsWindow(new SettingsWindowSetup
        {
            Title = title,
            Sections = sections,
            Theme = theme,
            RectStore = new MemoryRectStore(title, options.SeedRect),
            ProductMark = new SvgImageSource(MarkUri),
            ProductName = "Brand Test Harness",
            ProductVersion = "0.0.0-dev",
            PageMaxWidth = 720,
        });

        // The teardown that must run whichever section is current: a probe in flight on a panel
        // that is not on screen outlives the window otherwise.
        window.Closed += (_, _) =>
        {
            panel?.Cancel();
            Log($"{title}: closed");
        };

        if (options.Fit) window.FitToPages();

        Script(window, title, options);
        Log($"{title}: opened, current {window.CurrentTag}");
        return window;
    }

    /// <summary>The rig's SVG mark as an icon source, with its load outcome logged: an icon that
    /// never appears is otherwise indistinguishable from one that failed to load.</summary>
    private static SvgImageSource SvgIcon(string title, string tag, bool rasterise)
    {
        var svg = new SvgImageSource(MarkUri);
        if (rasterise)
        {
            svg.RasterizePixelWidth = 28;
            svg.RasterizePixelHeight = 28;
        }
        svg.Opened += (_, _) => Log($"{title}: svg icon {tag} opened");
        svg.OpenFailed += (_, e) => Log($"{title}: svg icon {tag} failed {e.Status}");
        return svg;
    }

    /// <summary>A page built from the row vocabulary in code, with the three controls that drive
    /// the shell from inside a page: rebuild everything, rebuild the build-once section (refused),
    /// and fit the window to its pages.</summary>
    private static UIElement BuildGeneral(int build, Func<SettingsWindow> window)
    {
        var refusal = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

        var rebuildAll = new Button { Content = "Rebuild all sections" };
        rebuildAll.Click += (_, _) => window().Rebuild();

        var rebuildPanel = new Button { Content = "Rebuild Home Assistant" };
        rebuildPanel.Click += (_, _) =>
        {
            try
            {
                window().Rebuild("homeassistant");
                refusal.Text = "Rebuilt — which the build-once flag should have refused.";
            }
            catch (InvalidOperationException ex)
            {
                refusal.Text = ex.Message;
            }
            refusal.Visibility = Visibility.Visible;
        };

        var fit = new Button { Content = "Fit to pages" };
        fit.Click += (_, _) => window().FitToPages();

        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "General", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                new SettingsSectionHeader
                {
                    Heading = "This page",
                    ShowDivider = false,
                    Info = "Built in code from the row vocabulary. The count says how many times the shell has built it.",
                },
                new SettingsRow
                {
                    Header = "Built",
                    Description = "Rises on every rebuild; the Home Assistant page's count never does.",
                    Field = new TextBlock { Text = $"{build} time{(build == 1 ? "" : "s")}", VerticalAlignment = VerticalAlignment.Center },
                },
                new SettingsRow
                {
                    Header = "Rebuild",
                    Description = "Every section that is not build-once is discarded and built again.",
                    Field = rebuildAll,
                },
                new SettingsRow
                {
                    Header = "Rebuild the build-once section",
                    Description = "Naming a build-once section is refused; the message shows beneath.",
                    Field = rebuildPanel,
                },
                refusal,
                new SettingsRow
                {
                    Header = "Fit",
                    Description = "Grows the window to the tallest page.",
                    Field = fit,
                },
                new SettingsSectionHeader
                {
                    Heading = "Rows",
                    Info = "The same vocabulary the MQTT panel is built on, so the two pages line up.",
                },
                new SettingsRow
                {
                    Header = "Host",
                    FieldWidth = 240,
                    Info = "A host name or an address; the port is separate.",
                    Field = new TextBox { Text = "mqtt.example.com", TextAlignment = TextAlignment.Right },
                },
                new SettingsRow
                {
                    Header = "Publish",
                    Description = "Off by default.",
                    Field = new ToggleSwitch { OnContent = "On", OffContent = "Off" },
                },
                new SettingsRow
                {
                    Header = "Interval",
                    FieldWidth = 240,
                    Field = new ComboBox { Items = { "10 s", "30 s", "60 s" }, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch },
                },
            },
        };
    }

    /// <summary>The scripted steps, 800 ms apart once the window is up: each navigation in turn,
    /// then the maximise, then the close — the actions a capture or a log needs taken without a
    /// hand on the mouse.</summary>
    private static void Script(SettingsWindow window, string title, Options options)
    {
        var steps = new Queue<Action>();
        foreach (var tag in options.NavigateTo)
            steps.Enqueue(() => window.Navigate(tag));
        if (options.Rebuild)
            steps.Enqueue(() =>
            {
                window.Rebuild();
                Log($"{title}: rebuilt, current {window.CurrentTag}");
            });
        if (options.Maximise)
            steps.Enqueue(() =>
            {
                ((OverlappedPresenter)window.AppWindow.Presenter).Maximize();
                Log($"{title}: maximised");
            });
        if (steps.Count == 0 && options.CloseAfterMs == 0) return;

        var queue = DispatcherQueue.GetForCurrentThread();
        var stepTimer = queue.CreateTimer();
        stepTimer.Interval = TimeSpan.FromMilliseconds(800);
        stepTimer.IsRepeating = true;
        stepTimer.Tick += (_, _) =>
        {
            if (steps.TryDequeue(out var step)) step();
            else stepTimer.Stop();
        };
        stepTimer.Start();

        if (options.CloseAfterMs > 0)
        {
            var closeTimer = queue.CreateTimer();
            closeTimer.Interval = TimeSpan.FromMilliseconds(options.CloseAfterMs);
            closeTimer.IsRepeating = false;
            closeTimer.Tick += (_, _) =>
            {
                Log($"{title}: closing at {Describe(new WindowRect(
                    window.AppWindow.Position.X, window.AppWindow.Position.Y,
                    window.AppWindow.Size.Width, window.AppWindow.Size.Height))}, " +
                    $"{((OverlappedPresenter)window.AppWindow.Presenter).State}");
                window.Close();
            };
            closeTimer.Start();
        }
    }

    /// <summary>Moves a window by an offset, the way a user drags one, so two rigs opened on the
    /// same monitor do not sit exactly on top of each other.</summary>
    public static void Nudge(SettingsWindow window, int offset)
    {
        var position = window.AppWindow.Position;
        window.AppWindow.Move(new PointInt32(position.X + offset, position.Y + offset));
    }

    public static WindowRect? ParseRect(string? text)
    {
        if (text is null) return null;
        var parts = text.Split(',');
        return parts.Length == 4 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y)
            && int.TryParse(parts[2], out int w) && int.TryParse(parts[3], out int h)
            ? new WindowRect(x, y, w, h)
            : null;
    }

    private static string Describe(WindowRect r) => $"{r.X},{r.Y} {r.Width}x{r.Height}";

    private static void Log(string line)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}"); }
        catch (IOException) { }
    }
}
