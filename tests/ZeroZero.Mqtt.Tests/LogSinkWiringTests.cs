using Xunit;
using ZeroZero.Primitives;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The log sink at the connection's seam when the host supplies nothing.</summary>
public class LogSinkWiringTests
{
    [Fact]
    public void AHostThatSuppliesNothingGetsTheSharedNoOp() =>
        // The one instance, not a fresh no-op per setup: a host tells "nothing wired" by identity,
        // and the publisher above the connection defaults to the same object.
        Assert.Same(NullLogSink.Instance, new MqttConnectionSetup { TopicRoot = "app" }.Log);
}
