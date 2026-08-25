using ZeroZero.Mqtt;
using ZeroZero.Mqtt.WinUI;

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
    private sealed class FileLog : IMqttLog
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

    /// <summary>A panel wired to invented state: connected, publishing recently, and with a command
    /// received a couple of hours ago, so all four Status rows read as they would in use.</summary>
    public static MqttPanelSetup Build()
    {
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

        return new MqttPanelSetup
        {
            Log = new FileLog(),
            Settings = store,
            Groups = groups,
            TopicRoot = "harness",
            Activity = activity,
            ConnectionState = () => MqttConnectionState.Connected,
            PublishNow = () => Task.FromResult(true),
            ConnectionChanged = () => { },
            PublishSetChanged = () => { },
            RecallEndpoint = () => memory,
            // Pinned for the same reason as the device id: the resolved default would carry the
            // machine name into the placeholder and into every screenshot.
            DefaultDeviceName = "Harness (demo)",
            PublishTitle = "Publish to MQTT",
            PublishDescription = "Publishes this rig's invented state to an MQTT broker.",
            PublishGroupsInfo = "Switching a group off withdraws its entities from the broker; "
                              + "switching it back on announces them again.",
            DeviceIdConsequence = "This rig publishes nothing, so nothing here has a consequence.",
            CommandLabel = id => id == "quiet_mode" ? "Quiet mode" : id,
        };
    }
}
