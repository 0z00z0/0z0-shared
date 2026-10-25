using ZeroZero.Mqtt;
using ZeroZero.Mqtt.WinUI;
using ZeroZero.Primitives;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Fabricated state for the MQTT settings panel, so every row can be seen populated without a broker
/// anywhere. Nothing here touches the network: the connection state is a constant, the endpoint
/// memory is made up, and "Publish now" answers without sending anything.
/// </summary>
internal static class MqttPanelSample
{
    /// <summary>Somewhere for the panel to say what went wrong. A WinExe has no console, and the
    /// panel swallows its own exceptions into this interface — without it a handler that throws is
    /// indistinguishable from one that did nothing.</summary>
    private sealed class FileLog : ILogSink
    {
        private static readonly string Path =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mqtt-harness-log.txt");

        public void Info(string message) => Append($"INFO  {message}");

        public void Error(string source, Exception? ex) => Append($"ERROR {source}: {ex}");

        private static void Append(string line)
        {
            try { File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}"); }
            catch (IOException) { }
        }
    }

    /// <summary>The store the panel reads and writes. In memory for the life of the rig, so a run
    /// leaves nothing behind on the machine it was seen on.</summary>
    private sealed class MemoryStore : IMqttSettingsStore
    {
        private MqttSettings _settings;

        public MemoryStore(MqttSettings seed) => _settings = seed;

        public MqttSettings Read() => _settings.Copy();

        public void Update(Action<MqttSettings> mutate)
        {
            var draft = _settings.Copy();
            mutate(draft);
            _settings = draft;
            Changed?.Invoke();
        }

        public event Action? Changed;
    }

    /// <summary>Set by <c>--link-up</c>: the link starts searching and comes up this long after
    /// construction, signalled from a background thread as a real connection does.</summary>
    internal static TimeSpan? LinkComesUpAfter { get; set; }

    /// <summary>A panel wired to invented state: connected, publishing recently, and with a command
    /// received a couple of hours ago, so all four Status rows read as they would in use.</summary>
    public static MqttPanelSetup Build()
    {
        var state = LinkComesUpAfter is null ? MqttConnectionState.Connected : MqttConnectionState.Searching;

        var store = new MemoryStore(new MqttSettings
        {
            Enabled = true,
            Host = "mqtt.example.com",
            Username = "harness",
            Password = "not-a-real-password",
            DeviceName = "",
            // Pinned rather than derived from the machine name, so a screenshot of this rig carries
            // no fact about whichever machine it was taken on.
            DeviceId = "harness_demo",
            DiscoveryPrefix = MqttSettings.DefaultDiscoveryPrefix,
        });

        var groups = new PublishGroupSet(store,
        [
            new("state", "State",
                Info: "What the machine is doing right now, sampled as it changes."),
            new("metrics", "Metrics",
                Description: "Off by default: these describe the application, not the hardware.",
                DefaultOn: false,
                Info: "Version, uptime and the application's own health counters. Useful for watching "
                    + "a fleet, noise on a single machine."),
            // Declared with no info text at all, so the row renders without an icon rather than with
            // one that opens on nothing.
            new("controls", "Controls"),
        ]);

        var activity = new MqttActivity();
        activity.RecordPublish(DateTimeOffset.UtcNow.AddMinutes(-3));
        activity.RecordCommand("quiet_mode", DateTimeOffset.UtcNow.AddHours(-2));

        var memory = new MqttEndpointMemory("mqtt.example.com", "harness", 8883, MqttTransport.Tcp, true);

        // Never applied, so it opens nothing; only it can back the removal card, whose click then
        // meets the real no-connection outcome.
        var neverApplied = new MqttConnection(new MqttConnectionSetup { TopicRoot = "harness" });

        var setup = new MqttPanelSetup(neverApplied)
        {
            OfferRemoval = true,
            Log = new FileLog(),
            Settings = store,
            Groups = groups,
            TopicRoot = "harness",
            Activity = activity,
            ConnectionState = () => state,
            PublishNow = () => Task.FromResult(true),
            ConnectionChanged = () => { },
            PublishSetChanged = () => { },
            RecallEndpoint = () => memory,
            // Pinned for the same reason as the device id: the resolved default would carry the
            // machine name into the placeholder and into every screenshot.
            DefaultDeviceName = "Harness (demo)",
            PublishTitle = "Publish to MQTT",
            PublishDescription = "Publishes this rig's invented state to a remote control server.",
            DeviceIdConsequence = "This rig publishes nothing, so nothing here has a consequence.",
            CommandLabel = id => id == "quiet_mode" ? "Quiet mode" : id,
        };

        if (LinkComesUpAfter is { } after)
            _ = Task.Delay(after).ContinueWith(_ =>
            {
                state = MqttConnectionState.Connected;
                setup.NotifyConnectionStateChanged();
            }, TaskScheduler.Default);

        return setup;
    }
}
