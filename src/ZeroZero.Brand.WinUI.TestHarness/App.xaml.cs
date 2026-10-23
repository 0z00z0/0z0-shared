using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ZeroZero.Controls.WinUI;
using ZeroZero.Tray;
using ZeroZero.SettingsShell.WinUI;
using ZeroZero.Win32;
using static ZeroZero.Brand.WinUI.TestHarness.HarnessArguments;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>The manual test rig: one scenario per run, chosen by the command line. The modes and
/// their flags are listed in docs/test-harness.md.</summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        // A WinExe has nowhere to print: without this a XAML-level failure is an exit code and
        // nothing else.
        UnhandledException += (_, e) =>
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "mqtt-harness-unhandled.txt"),
                              e.Exception.ToString());
    }

    private AboutScenario? _about;
    private MqttPanelScenario? _mqtt;
    private readonly List<BrandPaletteWindow> _paletteWindows = [];
    private readonly List<SettingsRowsWindow> _rowWindows = [];
    private readonly List<TitleBarWindow> _titleBarWindows = [];
    private readonly List<ZeroZero.SettingsShell.WinUI.SettingsWindow> _settingsWindows = [];

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One scenario per run: windows of unrelated scenarios would land on top of each other.
        // Read from the process, because an unpackaged launch carries no activation arguments.
        var commandLine = Environment.GetCommandLineArgs();
        if (ValueAfter(commandLine, "--culture") is { Length: > 0 } culture)
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        if (Has(commandLine, "--tray"))
        {
            if (Has(commandLine, "--anchor")) ShowAnchor();
            ShowTray(ValueAfter(commandLine, "--probe"),
                     Has(commandLine, "--file"),
                     Has(commandLine, "--menu"),
                     ValueAfter(commandLine, "--menu-theme") switch
                     {
                         "Light" => ElementTheme.Light,
                         "Dark" => ElementTheme.Dark,
                         _ => null,
                     },
                     ValueAfter(commandLine, "--place") switch
                     {
                         "NotificationArea" => TrayIconPlacement.NotificationArea,
                         "Overflow" => TrayIconPlacement.Overflow,
                         _ => null,
                     },
                     int.TryParse(ValueAfter(commandLine, "--restore-after"), out int restoreMs) ? restoreMs : 0,
                     Guid.TryParse(ValueAfter(commandLine, "--icon-id"), out Guid iconId) ? iconId : null);
            return;
        }

        if (Has(commandLine, "--native"))
        {
            ShowNativeDialogs(Has(commandLine, "--links"), Has(commandLine, "--stock"));
            return;
        }

        if (Has(commandLine, "--update"))
        {
            ShowUpdate(commandLine);
            return;
        }

        if (Has(commandLine, "--palette"))
        {
            ShowPalettes(ValueAfter(commandLine, "--probe"));
            return;
        }

        if (Has(commandLine, "--rows"))
        {
            ShowRows(ValueAfter(commandLine, "--probe"));
            return;
        }

        if (Has(commandLine, "--titlebar"))
        {
            ShowTitleBars();
            return;
        }

        if (Has(commandLine, "--settings"))
        {
            ShowSettingsShell(commandLine);
            return;
        }

        if (Has(commandLine, "--prompt"))
        {
            PromptScenario.Show(ValueAfter(commandLine, "--confirm"), Has(commandLine, "--cancel"));
            return;
        }

        if (Has(commandLine, "--mqtt"))
        {
            if (Has(commandLine, "--anchor")) ShowAnchor();
            _mqtt = new MqttPanelScenario();
            _mqtt.Show(commandLine);
            return;
        }

        _about = new AboutScenario(ShowAnchor);
        _about.Show(commandLine);
    }

    private Window? _anchorWindow;

    /// <summary>A patch of pure white in a corner of the work area: a capture that finds anything
    /// else there is of a dimmed or locked screen. It keeps the process alive after the popup closes.</summary>
    private void ShowAnchor()
    {
        var (workArea, _) = MonitorMetrics.ForCursor();

        _anchorWindow = new Window
        {
            Title = "White Anchor",
            Content = new Grid { Background = new SolidColorBrush(Colors.White) },
        };

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        _anchorWindow.AppWindow.SetPresenter(presenter);
        SwitcherVisibility.TrySet(_anchorWindow.AppWindow, shown: false);
        _anchorWindow.AppWindow.MoveAndResize(
            new Windows.Graphics.RectInt32(workArea.Left + 8, workArea.Top + 8, 160, 80));
        _anchorWindow.Activate();
    }

    private TrayScenario? _tray;

    /// <summary>The tray host on screen, with no window: the icon, tooltip and menu of the rig's own.</summary>
    private void ShowTray(string? probePath, bool ownFile, bool openMenu, ElementTheme? menuTheme,
                          TrayIconPlacement? placement, int restoreAfterMs, Guid? id)
    {
        _tray = new TrayScenario(probePath, ownFile, openMenu, menuTheme, placement, restoreAfterMs, id, Exit);
        _tray.Start();
    }

    /// <summary>The headless Win32 layer on screen: dark chrome applied, the task dialog with every
    /// part of its signature filled, then a message box reporting the id the dialog returned. The
    /// wording is the rig's own; the layer owns none.</summary>
    private void ShowNativeDialogs(bool commandLinks, bool stock)
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
            StockCancelButton = stock,
            SizeToContent = stock,
        });

        NativeMessageBox.Information(IntPtr.Zero, "Native Dialog Demo", $"The dialog returned {pressed}.");
        Exit();
    }

    /// <summary>The shared update window: a stage in both themes, the silent trigger, the window
    /// over the About popup, or a download cancelled part-way.</summary>
    private void ShowUpdate(string[] commandLine)
    {
        if (ValueAfter(commandLine, "--silent") is { Length: > 0 } silentProbe)
        {
            _ = UpdateScenario.RunSilentAsync(silentProbe, Exit);
            return;
        }

        if (ValueAfter(commandLine, "--cancel") is { Length: > 0 } cancelMode)
        {
            _ = UpdateCancelScenario.RunAsync(cancelMode, ValueAfter(commandLine, "--probe") ?? "cancel.txt", Exit);
            return;
        }

        if (ValueAfter(commandLine, "--over-about") is { Length: > 0 } mode)
        {
            _ = UpdateScenario.RunOverAboutAsync(mode, ValueAfter(commandLine, "--probe") ?? "over-about.txt", Exit);
            return;
        }

        // Opened first: the update window does not dismiss itself, so the order only decides which
        // window ends up in front, and the anchor belongs behind.
        if (Has(commandLine, "--anchor")) ShowAnchor();

        UpdateScenario.ShowStage(ValueAfter(commandLine, "--stage") ?? "question");
    }

    /// <summary>The brand dictionary on screen, one window per theme. The probe records the value
    /// that reached each element, the only place a key that missed shows.</summary>
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

    /// <summary>The settings-row vocabulary on screen: one window per theme at a settings page's
    /// width and one per theme under the card's wrap threshold, so the field dropping beneath its
    /// header is seen rather than assumed.</summary>
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

    /// <summary>Title-bar theming on screen, five Mica windows, one per treatment.</summary>
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

    /// <summary>The settings window shell on screen, one per theme; the second is nudged off the
    /// first so both stay reachable. Every hook and store call is logged to
    /// <c>settings-shell-log.txt</c> in the temp folder.</summary>
    private void ShowSettingsShell(string[] commandLine)
    {
        var options = new SettingsShellScenario.Options
        {
            Fit = Has(commandLine, "--fit"),
            SeedRect = SettingsShellScenario.ParseRect(ValueAfter(commandLine, "--rect")),
            NavigateTo = ValueAfter(commandLine, "--navigate")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [],
            Rebuild = Has(commandLine, "--rebuild"),
            Maximise = Has(commandLine, "--maximise"),
            CloseAfterMs = int.TryParse(ValueAfter(commandLine, "--close-after"), out int ms) ? ms : 0,
            BackdropTint = ValueAfter(commandLine, "--tint") is { Length: > 0 } tint
                ? new ZeroZero.SettingsShell.WinUI.BackdropTint
                  {
                      Light = BackdropColour.Parse(tint),
                      Dark = BackdropColour.Parse(tint),
                  }
                : null,
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

    private static void StartProbe(string path, Action<string> dump) =>
        ProbeFile.WriteAfter(TimeSpan.FromSeconds(2.5), path, dump);
}
