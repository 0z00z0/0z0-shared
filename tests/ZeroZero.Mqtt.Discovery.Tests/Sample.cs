namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The fixed vocabulary these tests assert against. A device id and a topic root that never
/// move, so a golden assertion is about the composition rather than about whatever the machine
/// happens to be called.</summary>
public static class Sample
{
    public const string TopicRoot = "exampleapp";
    public const string DeviceId = "exampleapp_desk01";
    public const string Prefix = "homeassistant";
    public const string DeviceName = "Example App (DESK01)";

    public static readonly MqttDeviceIdentity Identity = new(DeviceId, Prefix, DeviceName);

    public static readonly DiscoveryDevice Device =
        new("Example Vendor", "Example App", "1.4.0", "https://example.invalid/app");

    public static readonly DiscoveryOrigin Origin =
        new("Example App", "1.4.0", "https://example.invalid/support");

    public static string ConfigTopic => DiscoveryTopics.Device(Prefix, DeviceId);

    public static string State(string entityId) => MqttTopics.Channel(TopicRoot, DeviceId, entityId);

    public static string Command(string entityId) => MqttTopics.Command(TopicRoot, DeviceId, entityId);

    public static string Availability => MqttTopics.Availability(TopicRoot, DeviceId);

    public static string Withheld => MqttTopics.WithheldAvailability(TopicRoot, DeviceId);

    public static MqttSensor Sensor(
        string id = "cpu_load", string? value = "12", string? group = null, Func<bool>? include = null,
        bool retain = true) =>
        new()
        {
            EntityId = id,
            Name = "CPU load",
            Read = () => value,
            Unit = "%",
            StateClass = MqttStateClass.Measurement,
            Group = group,
            Include = include,
            Retain = retain,
        };

    public static MqttButton Button(string id = "restart", Action? press = null) => new()
    {
        EntityId = id,
        Name = "Restart",
        Press = () => MqttCommandVerdict.Accept(press ?? (() => { })),
    };

    public static MqttSelect Select(
        string id = "profile",
        Func<IReadOnlyList<string>>? options = null,
        Func<string?>? read = null,
        Action<string>? apply = null) => new()
        {
            EntityId = id,
            Name = "Profile",
            Options = options ?? (() => ["Office", "Home"]),
            Read = read ?? (() => "Office"),
            Apply = value => MqttCommandVerdict.Accept(() => apply?.Invoke(value)),
        };

    public static MqttSwitch Switch(
        string id = "quiet_mode", Func<bool?>? read = null, Action<bool>? apply = null,
        string? group = null, Func<bool>? include = null) => new()
        {
            EntityId = id,
            Name = "Quiet mode",
            Read = read ?? (() => true),
            Apply = on => MqttCommandVerdict.Accept(() => apply?.Invoke(on)),
            Group = group,
            Include = include,
        };

    public static MqttNumber Number(
        string id = "poll_interval", Func<double?>? read = null, Action<double>? apply = null) => new()
        {
            EntityId = id,
            Name = "Poll interval",
            Read = read ?? (() => 30),
            Apply = value => MqttCommandVerdict.Accept(() => apply?.Invoke(value)),
            Min = 5,
            Max = 300,
            Step = 5,
            Unit = "s",
            Mode = MqttNumberMode.Slider,
            Category = MqttEntityCategory.Config,
        };

    public static MqttText Text(string id = "note", Func<string?>? read = null) => new()
    {
        EntityId = id,
        Name = "Note",
        Read = read ?? (() => "hello"),
        Apply = _ => MqttCommandVerdict.Accept(() => { }),
        MaxLength = 20,
    };

    public static MqttBinarySensor BinarySensor(string id = "charging", Func<bool?>? read = null) => new()
    {
        EntityId = id,
        Name = "Charging",
        Read = read ?? (() => true),
        DeviceClass = "battery_charging",
    };
}
