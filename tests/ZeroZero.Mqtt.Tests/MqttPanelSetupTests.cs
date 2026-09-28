using Xunit;
using ZeroZero.Mqtt.WinUI;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The settings panel's setup object, compiled in from the panel's own project. What these
/// hold is that the Status rows and the closed Broker line describe one link: a setup built from the
/// connection must ask it at the moment each row is drawn, and a setup built without it must be
/// refused before the panel draws anything.</summary>
public class MqttPanelSetupTests
{
    private const string Root = "exampleapp";

    private static MqttPanelSetup FromConnection(MqttConnection connection)
    {
        var store = new RecordingSettingsStore();
        return new MqttPanelSetup(connection)
        {
            Settings = store,
            Groups = new PublishGroupSet(store, []),
            TopicRoot = Root,
            ConnectionChanged = () => { },
            PublishSetChanged = () => { },
        };
    }

    /// <summary>The live contradiction this construction exists to prevent: a Connection row reading
    /// connected above a Broker in use row reading "not connected yet", because the two were wired to
    /// different sources. Every accessor is read before the link exists and again after it, so a
    /// value captured at construction reads the first answer for ever.</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task ASetupBuiltFromTheConnectionFollowsItAfterConstruction()
    {
        using var broker = new FakeBroker();
        using var connection = new MqttConnection(new MqttConnectionSetup
        {
            TopicRoot = Root,
            Channels = [new("cpu_load", () => "42")],
        });
        var setup = FromConnection(connection);

        Assert.Same(connection.Activity, setup.Activity);
        Assert.Equal(MqttConnectionState.Disabled, setup.ConnectionState());
        Assert.NotNull(setup.RecallEndpoint);
        Assert.Null(setup.RecallEndpoint());
        Assert.False(await setup.PublishNow(), "nothing is connected yet, so nothing can be sent");

        await connection.ApplyAsync(new MqttConnectParameters
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = broker.Port,
            TransportMode = MqttTransportMode.Tcp,
            EncryptionMode = MqttEncryptionMode.Off,
            DeviceId = "desk01",
        });
        Assert.True(
            await FakeBroker.WaitAsync(() => connection.State == MqttConnectionState.Connected
                                             && connection.RememberedEndpoint is not null),
            "the connection never came up");

        Assert.Equal(MqttConnectionState.Connected, setup.ConnectionState());
        Assert.Equal(connection.RememberedEndpoint, setup.RecallEndpoint());
        Assert.Equal(broker.Port, setup.RecallEndpoint()?.Port);

        string topic = MqttTopics.Channel(Root, "desk01", "cpu_load");
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(topic) == 1));
        Assert.True(await setup.PublishNow(), "the setup's Publish now did not reach the live link");
        Assert.True(await FakeBroker.WaitAsync(() => broker.CountOn(topic) == 2));
    }

    /// <summary>The three connection members a panel cannot run without are no longer enforced by the
    /// compiler, so Initialise refuses a setup missing one, naming it — rather than a null
    /// dereference on the first Status refresh or the first press of Publish now.</summary>
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void ASetupWithoutTheConnectionIsRefusedForEachMemberItLacks()
    {
        var store = new RecordingSettingsStore();
        MqttPanelSetup Manual(bool activity = true, bool state = true, bool publish = true) => new()
        {
            Settings = store,
            Groups = new PublishGroupSet(store, []),
            TopicRoot = Root,
            ConnectionChanged = () => { },
            PublishSetChanged = () => { },
            Activity = activity ? new MqttActivity() : null!,
            ConnectionState = state ? () => MqttConnectionState.Disabled : null!,
            PublishNow = publish ? () => Task.FromResult(false) : null!,
        };

        Manual().EnsureConnectionMembers("setup");

        foreach (var (lacking, name) in new (MqttPanelSetup, string)[]
                 {
                     (Manual(activity: false), nameof(MqttPanelSetup.Activity)),
                     (Manual(state: false), nameof(MqttPanelSetup.ConnectionState)),
                     (Manual(publish: false), nameof(MqttPanelSetup.PublishNow)),
                 })
        {
            var refusal = Assert.Throws<ArgumentException>(() => lacking.EnsureConnectionMembers("setup"));
            Assert.Contains($"MqttPanelSetup.{name} ", refusal.Message, StringComparison.Ordinal);
            Assert.Equal("setup", refusal.ParamName);
        }
    }
}
