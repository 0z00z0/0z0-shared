using Xunit;
using ZeroZero.Primitives;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The log sink at the seam a host wires: what the publisher writes to when the host
/// supplies nothing, a plain sink of its own, or one typed on the module's own name.</summary>
public class LogSinkWiringTests
{
    /// <summary>A host's sink that is a sink and nothing else — not the module's own interface.</summary>
    private sealed class RecordingSink : ILogSink
    {
        public List<string> Lines { get; } = [];

        public void Info(string message) { lock (Lines) Lines.Add(message); }

        public void Error(string source, Exception? ex) { lock (Lines) Lines.Add($"{source}: {ex?.Message}"); }
    }

    /// <summary>The same, declared the way a host that wired the module before the sink was shared
    /// declares it.</summary>
    private sealed class ModuleNamedSink : IMqttLog
    {
        public List<string> Lines { get; } = [];

        public void Info(string message) => Lines.Add(message);

        public void Error(string source, Exception? ex) => Lines.Add($"{source}: {ex?.Message}");
    }

    private static DiscoveryPublisher Publisher(ILogSink? log) => new(new DiscoveryPublisherSetup
    {
        IsConnected = () => true,
        TopicRoot = Sample.TopicRoot,
        Device = Sample.Device,
        Origin = Sample.Origin,
        Entities = new MqttEntitySet([Sample.Sensor()]),
        Groups = new PublishGroupSet(new MemorySettingsStore(), []),
        Ledger = new RecordingLedgerStore(),
        SetChannelsAsync = (_, _) => Task.CompletedTask,
        SetCommandTargets = _ => { },
        BirthRepublishDelay = TimeSpan.Zero,
        Log = log ?? NullLogSink.Instance,
    });

    /// <summary>The one Info line the publisher writes without a broker in the way: a receiver's
    /// birth message. The line is what a host reads to know the module re-announced on its own.</summary>
    private static async Task ReceiverComesBackAsync(DiscoveryPublisher publisher)
    {
        await ((IMqttConnectionListener)publisher).OnConnectedAsync(
            new RecordingPublisher(), Sample.Identity, CancellationToken.None);
        var subscription = publisher.BirthMessage(Sample.Prefix);
        await subscription.Handler(
            new MqttInboundMessage(subscription.TopicFilter, "online", false), CancellationToken.None);
    }

    [Fact]
    public void AHostThatSuppliesNothingGetsTheSharedNoOp()
    {
        var setup = new DiscoveryPublisherSetup
        {
            IsConnected = () => true,
            TopicRoot = Sample.TopicRoot,
            Device = Sample.Device,
            Origin = Sample.Origin,
            Entities = new MqttEntitySet([]),
            Groups = null,
            Ledger = new RecordingLedgerStore(),
            SetChannelsAsync = DiscoveryWiring.NoChannelHandover,
            SetCommandTargets = DiscoveryWiring.NoCommandHandover,
        };

        Assert.Same(NullLogSink.Instance, setup.Log);
    }

    [Fact]
    public async Task AHostsOwnSinkReceivesWhatThePublisherWrites()
    {
        var sink = new RecordingSink();
        using var publisher = Publisher(sink);

        await ReceiverComesBackAsync(publisher);

        Assert.Contains("MQTT: the receiver announced itself; re-announcing the device.", sink.Lines);
    }

    [Fact]
    public async Task ASinkDeclaredUnderTheModulesOwnNameIsTheSameSeam()
    {
        var sink = new ModuleNamedSink();
        using var publisher = Publisher(sink);

        await ReceiverComesBackAsync(publisher);

        Assert.Contains("MQTT: the receiver announced itself; re-announcing the device.", sink.Lines);
    }
}
