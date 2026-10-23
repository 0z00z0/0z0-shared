using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static ZeroZero.Brand.WinUI.TestHarness.HarnessArguments;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>The MQTT settings panel on screen in its shapes: plain, branded, with rival overrides,
/// on Mica, with an invalid port, or narrowed to one window for a dialogue or a flyout. Flags:
/// docs/test-harness.md.</summary>
internal sealed class MqttPanelScenario
{
    private readonly List<MqttPanelWindow> _mqttWindows = [];
    private DispatcherQueueTimer? _settledTimer;
    private string? _onlyTitle;

    public void Show(string[] commandLine)
    {
        bool branded = Has(commandLine, "--brand");
        if (branded) InstallExtremePalette();
        if (Has(commandLine, "--controls")) InstallControlOverrides();

        // Either narrows the run to one window: a dialogue or a flyout is captured on a window
        // nothing else overlaps.
        string? dialogueOn = ValueAfter(commandLine, "--dialogue");
        string? infoOn = ValueAfter(commandLine, "--info");
        string? removeOn = ValueAfter(commandLine, "--remove");
        string? only = dialogueOn ?? infoOn ?? removeOn ?? ValueAfter(commandLine, "--only");
        if (ValueAfter(commandLine, "--link-up") is { Length: > 0 } seconds)
            MqttPanelSample.LinkComesUpAfter = TimeSpan.FromSeconds(double.Parse(seconds, CultureInfo.InvariantCulture));

        ShowMqttPanels(branded, Has(commandLine, "--mica"), Has(commandLine, "--error"), only);

        if (dialogueOn is { Length: > 0 }) OnceSettled(dialogueOn, window => window.OpenDeviceIdDialogue());
        if (infoOn is { Length: > 0 }) OnceSettled(infoOn, window => window.OpenFirstInfoBubble());
        if (removeOn is { Length: > 0 }) OnceSettled(removeOn, window => window.OpenRemovalDialogue());

        if (ValueAfter(commandLine, "--probe") is { Length: > 0 } probePath)
            ProbeFile.WriteAfter(TimeSpan.FromSeconds(2.5), probePath, path =>
            {
                foreach (var window in _mqttWindows) ThemeProbe.Dump(path, window.Title, window.ProbeRoot);
            });
    }

    /// <summary>Eight windows: both themes with the groups closed, the Broker group open, the publish
    /// list open and a staged edit. One group open per window, because both would be taller than
    /// the display. The titles name the capture files.</summary>
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
            // Both with the Broker group open: the controls an override has to reach are in it.
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

    /// <summary>A studio palette the stock theme cannot produce, declared as immediate entries of
    /// <see cref="Application.Resources"/> where the consumption guide says. Flat, not per theme, so
    /// "the override arrived" is one pixel comparison.</summary>
    private static void InstallExtremePalette()
    {
        var resources = Application.Current.Resources;
        resources["MqttPanelHeadingBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF));
        resources["MqttPanelBodyBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0xFF));
        resources["MqttPanelSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00));
        resources["MqttPanelAccentBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00));
        resources["MqttPanelCardBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x10, 0x40));
        resources["MqttPanelFontFamily"] = new FontFamily("Consolas");
    }

    /// <summary>Overrides at the shared-brush layer (what a control key aliases) and at the
    /// control-key layer (what its template looks up), each colour once, so the colour on screen
    /// names the layer that reached the control.</summary>
    private static void InstallControlOverrides()
    {
        var resources = Application.Current.Resources;

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

    /// <summary>Runs an action on one named window once it has settled. One window only: a second
    /// ContentDialog on the same thread never opens.</summary>
    private void OnceSettled(string windowTitle, Action<MqttPanelWindow> action)
    {
        _settledTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _settledTimer.Interval = TimeSpan.FromSeconds(1.0);
        _settledTimer.IsRepeating = false;
        _settledTimer.Tick += (_, _) =>
        {
            if (_mqttWindows.FirstOrDefault(w => w.Title == windowTitle) is { } window) action(window);
        };
        _settledTimer.Start();
    }
}
