using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ZeroZero.Brand.Core;
using ZeroZero.Controls.WinUI;
using ZeroZero.Win32;
// This project's own namespace nests inside ZeroZero.Brand (same collision documented in
// BrandAboutWindow.xaml.cs), so an unqualified "Brand" resolves to the namespace segment
// instead of ZeroZero.Brand.Core.Brand — alias it to sidestep that.
using CoreBrand = ZeroZero.Brand.Core.Brand;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Interactive manual test rig exercising both ways of hosting the shared About content, so each
/// can be eyeballed without building or running ChargeKeeper, HyperVManagerTray, or M365Migrator:
/// <list type="bullet">
/// <item><see cref="BrandAboutWindow"/> — the tray-app popup (Mica backdrop, centring, credits
/// expander, "Check for Updates").</item>
/// <item><see cref="HostedControlWindow"/> — <see cref="BrandAboutControl"/> embedded directly in
/// a plain window with ordinary chrome and no update button, simulating a full windowed app's
/// in-navigation About page.</item>
/// </list>
/// Both windows open at launch; the app exits once the last of the two is closed (default
/// <see cref="Application.DispatcherShutdownMode"/> is OnLastWindowClose).
/// <para>
/// <c>--mqtt</c> switches to the MQTT settings panel instead, in one of five shapes:
/// bare (eight windows, the screenshot set), <c>--brand</c> (an extreme studio palette declared
/// where the consumption guide says to declare it, so a capture shows how far the module's keys
/// reach), <c>--controls</c> (adds rival overrides at the shared-brush and control-key layers, so
/// one capture says which layer a control follows), <c>--mica</c> (no page background and a Mica
/// backdrop, the ground a rig otherwise never reproduces) and <c>--error</c> (an out-of-range port,
/// so the validation tier is on screen). <c>--dialogue "&lt;window title&gt;"</c> opens the
/// device-id dialogue on that one window and suppresses the rest, <c>--info "&lt;window title&gt;"</c>
/// does the same with the first info bubble's flyout, and <c>--probe &lt;path&gt;</c> writes
/// <see cref="ThemeProbe"/>'s numbers beside the capture.
/// </para>
/// <para>
/// <c>--palette</c> opens the brand resource dictionary instead, one window per theme, with every
/// key resolved through ThemeResource; <c>--probe &lt;path&gt;</c> writes the colour and face that
/// reached each element.
/// </para>
/// <para>
/// <c>--rows</c> opens the settings-row vocabulary instead: section headers and rows in every
/// shape a page uses, one window per theme at a page's width and one per theme narrow enough for
/// the card to drop its field beneath the header; <c>--probe &lt;path&gt;</c> works here too.
/// </para>
/// <para>
/// <c>--titlebar</c> opens four Mica windows with the system title bar: a dark page with the bar
/// untreated, so the light caption strip is on screen; the same page with the bar following its
/// theme; a light page following its theme; and a light page with the bar pinned dark.
/// </para>
/// <para>
/// <c>--prompt</c> opens the text prompt, one per theme; <c>--prompt --confirm "&lt;text&gt;"</c>
/// or <c>--prompt --cancel</c> opens one, answers it through its own field and button, writes
/// what its task resolved with to <c>text-prompt-result.txt</c> in the temp folder, and exits.
/// </para>
/// <para>
/// <c>--native</c> opens no XAML window at all: it shows the Win32 layer's task dialog with every
/// part of its signature filled (<c>--links</c> renders the buttons as command links), then a
/// message box naming the button pressed, and exits.
/// </para>
/// <para>
/// <c>--tray</c> opens no window either: it puts the tray host's icon in the notification area
/// from the rig's own drawing, tooltip and menu, and stays until Exit is chosen from the menu.
/// <c>--file</c> hands the host a file the rig wrote once instead of frames per render;
/// <c>--menu</c> opens the menu by the tray after two seconds, so a capture needs no click;
/// <c>--promote</c> puts the icon in the taskbar proper rather than the overflow, through the
/// shell's own per-icon setting, undone on exit; <c>--probe &lt;path&gt;</c> writes what the
/// host created to that path, logs every click beside it, and exits once a <c>.stop</c> file
/// appears beside the probe.
/// </para>
/// </summary>
public partial class App : Application
{
    private Window? _aboutWindow;
    private Window? _hostedControlWindow;

    public App()
    {
        InitializeComponent();
        // A WinExe has nowhere to print: without this a XAML-level failure is an exit code and
        // nothing else.
        UnhandledException += (_, e) =>
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "mqtt-harness-unhandled.txt"),
                              e.Exception.ToString());
    }

    private readonly List<MqttPanelWindow> _mqttWindows = [];
    private readonly List<BrandPaletteWindow> _paletteWindows = [];
    private readonly List<SettingsRowsWindow> _rowWindows = [];
    private readonly List<TitleBarWindow> _titleBarWindows = [];
    private readonly List<ZeroZero.SettingsShell.WinUI.SettingsWindow> _settingsWindows = [];
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _probeTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _settledTimer;
    private string? _onlyTitle;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One scenario per run: the About windows and the MQTT panel windows are captured by
        // separate scripts, and four unrelated windows in one run would land on top of each other.
        // Read from the process command line, because an unpackaged WinUI launch carries no
        // arguments on the activation event.
        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Any(a => a.Equals("--tray", StringComparison.Ordinal)))
        {
            ShowTray(ValueAfter(commandLine, "--probe"),
                     commandLine.Any(a => a.Equals("--file", StringComparison.Ordinal)),
                     commandLine.Any(a => a.Equals("--menu", StringComparison.Ordinal)),
                     commandLine.Any(a => a.Equals("--promote", StringComparison.Ordinal)));
            return;
        }

        if (commandLine.Any(a => a.Equals("--native", StringComparison.Ordinal)))
        {
            ShowNativeDialogs(commandLine.Any(a => a.Equals("--links", StringComparison.Ordinal)));
            return;
        }

        if (commandLine.Any(a => a.Equals("--palette", StringComparison.Ordinal)))
        {
            ShowPalettes(ValueAfter(commandLine, "--probe"));
            return;
        }

        if (commandLine.Any(a => a.Equals("--rows", StringComparison.Ordinal)))
        {
            ShowRows(ValueAfter(commandLine, "--probe"));
            return;
        }

        if (commandLine.Any(a => a.Equals("--titlebar", StringComparison.Ordinal)))
        {
            ShowTitleBars();
            return;
        }

        if (commandLine.Any(a => a.Equals("--settings", StringComparison.Ordinal)))
        {
            ShowSettingsShell(commandLine);
            return;
        }

        if (commandLine.Any(a => a.Equals("--prompt", StringComparison.Ordinal)))
        {
            ShowPrompts(ValueAfter(commandLine, "--confirm"),
                        commandLine.Any(a => a.Equals("--cancel", StringComparison.Ordinal)));
            return;
        }

        if (commandLine.Any(a => a.Equals("--mqtt", StringComparison.Ordinal)))
        {
            bool branded = commandLine.Any(a => a.Equals("--brand", StringComparison.Ordinal));
            if (branded) InstallExtremePalette();
            if (commandLine.Any(a => a.Equals("--controls", StringComparison.Ordinal))) InstallControlOverrides();

            // Either narrows the run to one window: a dialogue or a flyout is captured on a window
            // nothing else overlaps.
            string? dialogueOn = ValueAfter(commandLine, "--dialogue");
            string? infoOn = ValueAfter(commandLine, "--info");
            string? only = dialogueOn ?? infoOn;

            ShowMqttPanels(branded,
                           commandLine.Any(a => a.Equals("--mica", StringComparison.Ordinal)),
                           commandLine.Any(a => a.Equals("--error", StringComparison.Ordinal)),
                           only);

            if (dialogueOn is { Length: > 0 }) OnceSettled(dialogueOn, window => window.OpenDeviceIdDialogue());
            if (infoOn is { Length: > 0 }) OnceSettled(infoOn, window => window.OpenFirstInfoBubble());

            if (ValueAfter(commandLine, "--probe") is { Length: > 0 } probePath)
                StartProbe(probePath, path =>
                {
                    foreach (var window in _mqttWindows) ThemeProbe.Dump(path, window.Title, window.ProbeRoot);
                });
            return;
        }

        var libraries = new ExternalLibrary[]
        {
            new("Microsoft.WindowsAppSDK", "Microsoft", "WinUI 3 / Windows App SDK runtime", "MIT", "https://github.com/microsoft/WindowsAppSDK"),
            new("H.NotifyIcon.WinUI", "HavenDV", "Example third-party credit (not an actual dependency of this harness)", "MIT", "https://github.com/HavenDV/H.NotifyIcon"),
        };

        var options = new BrandAboutOptions
        {
            Info = new AboutInfo
            {
                AppName     = "Brand Test Harness",
                Version     = "0.0.0-dev",
                Description = "Interactive launch-test rig for the shared BrandAboutWindow component — " +
                              "renders the About box from this repo's own sample data, independently " +
                              "of any consuming app.",
                RepoUrl     = $"{CoreBrand.OrgUrl}/0z0-shared",
                ExternalLibraries = libraries,
            },
            // Present so the "Check for Updates" button is visible and clickable for the test —
            // omit this to verify the button hides itself instead (see BrandAboutWindow.xaml.cs).
            // Returns false (no update applied) so the window stays open for inspection rather than
            // driving the new exit flow.
            OnCheckForUpdates = async () => { await Task.Delay(500); return false; },
        };

        _aboutWindow = new BrandAboutWindow(options);
        // Distinct, recognizable titles so the capture script can tell the two windows apart even
        // though BrandAboutWindow hides its own title bar (the AppWindow title is still set).
        _aboutWindow.Title = "Window Mode";
        _aboutWindow.Activate();

        var hostedInfo = new AboutInfo
        {
            AppName     = "Brand Test Harness (hosted control)",
            Version     = "0.0.0-dev",
            Description = "Same BrandAboutControl content as the popup, hosted directly inside a plain " +
                          "window with ordinary chrome and no update button — simulating M365Migrator's " +
                          "in-navigation About page, which has no popup or update/exit concept.",
            RepoUrl     = $"{CoreBrand.OrgUrl}/0z0-shared",
            ExternalLibraries = libraries,
        };
        _hostedControlWindow = new HostedControlWindow(hostedInfo);
        _hostedControlWindow.Activate();
    }

    private TrayScenario? _tray;

    /// <summary>
    /// The tray host on screen: the icon in the notification area from the rig's own drawing, the
    /// tooltip and the menu, with no window at all. With a probe path the rig records what the host
    /// created and stays until a stop file appears; otherwise it stays until Exit is chosen.
    /// </summary>
    private void ShowTray(string? probePath, bool ownFile, bool openMenu, bool promote)
    {
        _tray = new TrayScenario(probePath, ownFile, openMenu, promote, Exit);
        _tray.Start();
    }

    /// <summary>
    /// The headless Win32 layer on screen: dark chrome applied, the task dialog with caption,
    /// headline, body, detail, icon and two buttons, then a message box reporting the id the dialog
    /// returned. The wording is the rig's own — the layer carries text, it owns none.
    /// </summary>
    private void ShowNativeDialogs(bool commandLinks)
    {
        DarkChrome.Apply(DarkChromeMode.AllowDark);

        int pressed = NativeTaskDialog.Show(IntPtr.Zero, new TaskDialogRequest
        {
            Caption = "Native Dialog Demo",
            Headline = "A headline beside the icon",
            Body = "The body paragraph, worded by the caller. The dialog carries it across and " +
                   "reports the id of the button pressed.",
            Detail = "Detail text, collapsed behind the toggle until asked for.",
            Icon = TaskDialogIcon.Information,
            Buttons =
            [
                new TaskDialogButton(100, commandLinks ? "First choice\nThe note beneath a command link" : "First choice"),
                new TaskDialogButton(101, commandLinks ? "Second choice\nAnother note" : "Second choice"),
            ],
            CommandLinks = commandLinks,
        });

        NativeMessageBox.Information(IntPtr.Zero, "Native Dialog Demo", $"The dialog returned {pressed}.");
        Exit();
    }

    /// <summary>
    /// Eight windows: each theme as the panel opens with both groups closed, each theme with the
    /// Broker group open, each theme with the publish list open, and each theme holding a staged
    /// edit behind a closed Broker group. One group open at a time, because a window holding both is
    /// taller than the display and a screenshot of it would prove nothing about the half that
    /// scrolled off. The titles are what the capture script names the files by.
    /// </summary>
    private void ShowMqttPanels(
        bool branded = false, bool mica = false, bool invalidPort = false, string? onlyTitle = null)
    {
        _onlyTitle = onlyTitle;

        if (invalidPort)
        {
            ShowScenarios(
            [
                ("MQTT Panel Light Error", ElementTheme.Light, true, false, false),
                ("MQTT Panel Dark Error", ElementTheme.Dark, true, false, false),
            ], invalidPort: true);
            return;
        }

        if (mica)
        {
            // Both groups closed: the surfaces exposed to the backdrop are the section headings, the
            // rules and the translucent card grounds, and all of those are on the opening view.
            ShowScenarios(
            [
                ("MQTT Panel Light Mica", ElementTheme.Light, false, false, false),
                ("MQTT Panel Dark Mica", ElementTheme.Dark, false, false, false),
            ], mica: true);
            return;
        }

        if (branded)
        {
            // Two windows only, and both with the Broker group open: the branded run exists to show
            // what an override reaches, and the controls it has to reach are in that group.
            ShowScenarios(
            [
                ("MQTT Panel Light Branded", ElementTheme.Light, true, false, false),
                ("MQTT Panel Dark Branded", ElementTheme.Dark, true, false, false),
            ]);
            return;
        }

        ShowScenarios(
        [
            ("MQTT Panel Light", ElementTheme.Light, false, false, false),
            ("MQTT Panel Dark", ElementTheme.Dark, false, false, false),
            ("MQTT Panel Light Broker", ElementTheme.Light, true, false, false),
            ("MQTT Panel Dark Broker", ElementTheme.Dark, true, false, false),
            ("MQTT Panel Light Groups", ElementTheme.Light, false, true, false),
            ("MQTT Panel Dark Groups", ElementTheme.Dark, false, true, false),
            ("MQTT Panel Light Edited", ElementTheme.Light, false, false, true),
            ("MQTT Panel Dark Edited", ElementTheme.Dark, false, false, true),
        ]);
    }

    private void ShowScenarios(
        (string Title, ElementTheme Theme, bool Broker, bool Publish, bool Edited)[] scenarios,
        bool mica = false,
        bool invalidPort = false)
    {
        // A dialogue run opens one window and no more: eight cascaded windows share the screen area
        // the dialogue occupies, so a click aimed at one of them cannot be aimed reliably at all.
        if (_onlyTitle is { Length: > 0 } wanted)
            scenarios = scenarios.Where(s => s.Title == wanted).ToArray();

        for (int i = 0; i < scenarios.Length; i++)
        {
            var (title, theme, broker, publish, edited) = scenarios[i];
            try
            {
                var window = new MqttPanelWindow(
                    title, theme, broker, publish, edited, offset: i * 50, mica, invalidPort);
                _mqttWindows.Add(window);
                window.Activate();
            }
            catch (Exception ex)
            {
                // A WinExe has nowhere to print, and a window that never appears is otherwise
                // indistinguishable from one that failed silently.
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "mqtt-harness-error.txt"), ex.ToString());
                throw;
            }
        }
    }

    /// <summary>
    /// A studio palette nothing in the stock theme could produce, declared exactly where the
    /// consumption guide says to declare it — as immediate entries of
    /// <see cref="Application.Resources"/>, which outrank the module's merged defaults. Flat rather
    /// than per-theme on purpose: a colour that is the same in light and dark makes "the override
    /// arrived" a single pixel comparison instead of a judgement.
    /// </summary>
    private static void InstallExtremePalette()
    {
        var resources = Current.Resources;
        resources["MqttPanelHeadingBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF));
        resources["MqttPanelBodyBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0xFF));
        resources["MqttPanelSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00));
        resources["MqttPanelAccentBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00));
        resources["MqttPanelCardBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x10, 0x40));
        resources["MqttPanelFontFamily"] = new FontFamily("Consolas");
    }

    /// <summary>
    /// The two rival explanations for why control chrome stays stock, installed side by side so one
    /// capture separates them. The shared-brush layer is what a control key aliases; the control-key
    /// layer is what its template looks up. Each colour appears once, so whichever shows on screen
    /// names the layer that reached the control.
    /// </summary>
    private static void InstallControlOverrides()
    {
        var resources = Current.Resources;

        // Shared semantic brushes: the targets the control keys alias.
        resources["TextFillColorPrimaryBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0x00));
        resources["ControlFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00));
        resources["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00));

        // The controls' own keys.
        resources["ComboBoxForeground"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0x80));
        resources["ComboBoxBackground"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x40, 0x00, 0x00));
        resources["TextControlForeground"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x80, 0xFF, 0x00));
        resources["TextControlBackground"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x00, 0x40));
        resources["SettingsCardBackground"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x40, 0x40, 0x00));
        resources["ButtonForeground"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0x80));
        resources["ButtonBackground"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x40, 0x40));
    }

    /// <summary>Runs an action on one named window once it has settled, so a dialogue or a flyout
    /// can be captured with its own text tiers on screen. One window only: a second ContentDialog
    /// on the same thread never opens.</summary>
    private void OnceSettled(string windowTitle, Action<MqttPanelWindow> action)
    {
        _settledTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _settledTimer.Interval = TimeSpan.FromSeconds(1.0);
        _settledTimer.IsRepeating = false;
        _settledTimer.Tick += (_, _) =>
        {
            if (_mqttWindows.FirstOrDefault(w => w.Title == windowTitle) is { } window) action(window);
        };
        _settledTimer.Start();
    }

    private static string? ValueAfter(string[] commandLine, string option)
    {
        int index = Array.IndexOf(commandLine, option);
        return index >= 0 && index + 1 < commandLine.Length ? commandLine[index + 1] : null;
    }

    /// <summary>
    /// The brand dictionary on screen, one window per theme. What a consumer resolves through
    /// ThemeResource is seen here rather than read off the markup, and the probe records the value
    /// that reached each element, which is the only place a key that missed shows.
    /// </summary>
    private void ShowPalettes(string? probePath)
    {
        (string Title, ElementTheme Theme)[] scenarios =
        [
            ("Brand Palette Light", ElementTheme.Light),
            ("Brand Palette Dark", ElementTheme.Dark),
        ];
        for (int i = 0; i < scenarios.Length; i++)
        {
            var window = new BrandPaletteWindow(scenarios[i].Title, scenarios[i].Theme, offset: i * 50);
            _paletteWindows.Add(window);
            window.Activate();
        }

        if (probePath is { Length: > 0 })
            StartProbe(probePath, path =>
            {
                foreach (var window in _paletteWindows) window.Probe(path);
            });
    }

    /// <summary>
    /// The settings-row vocabulary on screen: one window per theme at a settings page's width, and
    /// one per theme at a width under the toolkit card's wrap threshold, so the field dropping
    /// beneath its header is seen rather than assumed.
    /// </summary>
    private void ShowRows(string? probePath)
    {
        (string Title, ElementTheme Theme, double Width)[] scenarios =
        [
            ("Settings Rows Light", ElementTheme.Light, 780),
            ("Settings Rows Dark", ElementTheme.Dark, 780),
            ("Settings Rows Light Narrow", ElementTheme.Light, 420),
            ("Settings Rows Dark Narrow", ElementTheme.Dark, 420),
        ];
        for (int i = 0; i < scenarios.Length; i++)
        {
            var window = new SettingsRowsWindow(scenarios[i].Title, scenarios[i].Theme, scenarios[i].Width, offset: i * 50);
            _rowWindows.Add(window);
            window.Activate();
        }

        if (probePath is { Length: > 0 })
            StartProbe(probePath, path =>
            {
                foreach (var window in _rowWindows) ThemeProbe.Dump(path, window.Title, window.ProbeRoot);
            });
    }

    /// <summary>
    /// Title-bar theming on screen, four Mica windows: a dark page with its bar untreated (the
    /// light caption strip), the same page with the bar following its theme, a light page
    /// following its theme (the bar stays stock), and a light page with the bar pinned dark.
    /// </summary>
    private void ShowTitleBars()
    {
        (string Title, ElementTheme Theme, TitleBarWindow.Treatment Treatment)[] scenarios =
        [
            ("Title Bar Dark Untreated", ElementTheme.Dark, TitleBarWindow.Treatment.None),
            ("Title Bar Dark", ElementTheme.Dark, TitleBarWindow.Treatment.FollowTheme),
            ("Title Bar Light", ElementTheme.Light, TitleBarWindow.Treatment.FollowTheme),
            ("Title Bar Light Fixed Dark", ElementTheme.Light, TitleBarWindow.Treatment.FixedDark),
            ("Title Bar Light Reverted", ElementTheme.Light, TitleBarWindow.Treatment.DarkThenLight),
        ];
        for (int i = 0; i < scenarios.Length; i++)
        {
            var window = new TitleBarWindow(scenarios[i].Title, scenarios[i].Theme, scenarios[i].Treatment, offset: i * 60);
            _titleBarWindows.Add(window);
            window.Activate();
        }
    }

    /// <summary>
    /// The settings window shell on screen, one per theme, each with four sections: a page from
    /// the row vocabulary, the MQTT panel built once, a timer page and the About control. The
    /// second window is nudged off the first so both stay reachable. <c>--only Light|Dark</c>
    /// opens one; <c>--fit</c> fits it to its pages; <c>--rect X,Y,W,H</c> seeds the rectangle
    /// store, so the clamp can be seen; <c>--navigate a,b,c</c> walks the sections, and
    /// <c>--rebuild</c>, <c>--maximise</c> and <c>--close-after &lt;ms&gt;</c> take the steps a
    /// saved-rectangle measurement needs. Every hook and every store call is logged to
    /// <c>settings-shell-log.txt</c> in the temp folder.
    /// </summary>
    private void ShowSettingsShell(string[] commandLine)
    {
        var options = new SettingsShellScenario.Options
        {
            Fit = commandLine.Any(a => a.Equals("--fit", StringComparison.Ordinal)),
            SeedRect = SettingsShellScenario.ParseRect(ValueAfter(commandLine, "--rect")),
            NavigateTo = ValueAfter(commandLine, "--navigate")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [],
            Rebuild = commandLine.Any(a => a.Equals("--rebuild", StringComparison.Ordinal)),
            Maximise = commandLine.Any(a => a.Equals("--maximise", StringComparison.Ordinal)),
            CloseAfterMs = int.TryParse(ValueAfter(commandLine, "--close-after"), out int ms) ? ms : 0,
        };
        string? only = ValueAfter(commandLine, "--only");

        (string Title, ElementTheme Theme)[] scenarios =
        [
            ("Settings Shell Light", ElementTheme.Light),
            ("Settings Shell Dark", ElementTheme.Dark),
        ];
        int opened = 0;
        foreach (var (title, theme) in scenarios)
        {
            if (only is { Length: > 0 } && !title.EndsWith(only, StringComparison.OrdinalIgnoreCase)) continue;
            var window = SettingsShellScenario.Open(title, theme, options);
            _settingsWindows.Add(window);
            if (opened > 0) SettingsShellScenario.Nudge(window, 60 * opened);
            window.Activate();
            opened++;
        }
    }

    /// <summary>
    /// The text prompt on screen, one per theme, each with a note beneath the field. With
    /// <c>--confirm &lt;text&gt;</c> or <c>--cancel</c> one prompt opens instead, is answered
    /// through its own field and button once settled, and the value its task resolved with is
    /// written to <c>text-prompt-result.txt</c> in the temp folder before the rig exits — so the
    /// completion path is measured, not read.
    /// </summary>
    private async void ShowPrompts(string? confirmWith, bool cancel)
    {
        static TextPromptOptions Options(ElementTheme theme, string title) => new()
        {
            Title = title,
            Message = "The name the device is announced under. Entities keep their ids; only the "
                    + "label shown for the device changes.",
            Confirm = "Rename",
            Note = "Applies on the next publish.",
            InitialText = "Harness (demo)",
            Placeholder = "A name for this device",
            MaxLength = 64,
            Theme = theme,
        };

        if (confirmWith is null && !cancel)
        {
            _ = TextPromptWindow.ShowAsync(Options(ElementTheme.Light, "Text Prompt Light"));
            _ = TextPromptWindow.ShowAsync(Options(ElementTheme.Dark, "Text Prompt Dark"));
            return;
        }

        var window = new TextPromptWindow(Options(ElementTheme.Dark, "Text Prompt Driven"));
        window.Activate();
        // The rig reaches into the realised tree, as it does for the panel: the field and the
        // buttons are the prompt's own and nothing an application would call belongs on it for a
        // rig's sake.
        await Task.Delay(1500);
        if (window.Content is FrameworkElement root)
        {
            // --type "<text>" edits the field before either answer, so a cancel after an edit is
            // its own case and not only a cancel of the untouched, still-selected initial text.
            string? typed = confirmWith ?? ValueAfter(Environment.GetCommandLineArgs(), "--type");
            if (typed is not null && FindDescendant<TextBox>(root) is { } field) field.Text = typed;
            string wanted = cancel ? "Cancel" : "Rename";
            if (FindButton(root, wanted) is { } button &&
                FrameworkElementAutomationPeer.CreatePeerForElement(button) is IInvokeProvider invoke)
                invoke.Invoke();
        }
        string? result = await window.Result;
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "text-prompt-result.txt"), result ?? "<null>");
        // No Exit(): the prompt was the last window, so the rig is already shutting down, and an
        // Exit() on top of that shutdown took the process down with an access violation.
    }

    private static Button? FindButton(DependencyObject root, string content)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button { Content: string text } button && text == content) return button;
            if (FindButton(child, content) is { } found) return found;
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// Runs a dump to a tab-separated file once layout has settled, so the capture and the numbers
    /// come from the same run.
    /// </summary>
    private void StartProbe(string path, Action<string> dump)
    {
        // Held in a field: a local timer is unrooted and can be collected before it ever ticks.
        _probeTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _probeTimer.Interval = TimeSpan.FromSeconds(2.5);
        _probeTimer.IsRepeating = false;
        _probeTimer.Tick += (_, _) =>
        {
            try
            {
                File.WriteAllText(path, "");
                dump(path);
            }
            catch (Exception ex)
            {
                File.WriteAllText(path + ".error.txt", ex.ToString());
            }
        };
        _probeTimer.Start();
    }
}
