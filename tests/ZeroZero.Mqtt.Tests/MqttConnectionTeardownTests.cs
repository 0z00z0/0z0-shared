using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>What a settings change does when it arrives after the connection has been torn down. A
/// host applies on every settings change and disposes on exit, and the two are not ordered, so the
/// late apply is a real arrival rather than a hypothetical one.</summary>
public class MqttConnectionTeardownTests
{
    private static MqttConnectParameters Parameters() => new()
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = 1,
        TransportMode = MqttTransportMode.Tcp,
        EncryptionMode = MqttEncryptionMode.Off,
        DeviceId = "desk01",
    };

    /// <summary>The reconcile is refused outright rather than half-run: the identity is not taken, so
    /// no maintain loop is started against a client that is already gone.</summary>
    [Fact]
    public async Task ApplyingAfterDisposalReconcilesNothing()
    {
        var connection = new MqttConnection(new MqttConnectionSetup { TopicRoot = "exampleapp" });
        connection.Dispose();

        await connection.ApplyAsync(Parameters());

        Assert.Equal("", connection.DeviceId);
    }

    /// <summary>The race the check above cannot close: the teardown lands after a reconcile has
    /// passed it and is inside the gate, so the release runs against a gate the teardown has already
    /// been through. The endpoint recall is the hook because it is called inside the gate.</summary>
    /// <remarks>The connection is left with a maintain loop it cannot connect on — the reconcile ran
    /// to its end — which costs a refused socket on a dead port and nothing else.</remarks>
    [Fact]
    public async Task DisposingFromInsideAReconcileLeavesTheReleaseWithSomethingToReleaseInto()
    {
        MqttConnection? connection = null;
        var setup = new MqttConnectionSetup
        {
            TopicRoot = "exampleapp",
            RecallEndpoint = () => { connection!.Dispose(); return null; },
        };
        connection = new MqttConnection(setup);

        Assert.Null(await Record.ExceptionAsync(() => connection.ApplyAsync(Parameters())));
    }

    /// <summary>A second disposal is what a host that tears down explicitly and then disposes on the
    /// way out does, and it must cost nothing.</summary>
    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var connection = new MqttConnection(new MqttConnectionSetup { TopicRoot = "exampleapp" });
        connection.Dispose();

        Assert.Null(Record.Exception(connection.Dispose));
    }
}
