using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>Resolving a candidate to the wire. The one place either transport is spelled out, so
/// the live publisher and the connection check cannot address the broker differently.</summary>
public class MqttEndpointTests
{
    [Fact]
    public void Encrypts_IsTrueWhenAsked() =>
        Assert.True(MqttEndpoint.Encrypts(MqttTransport.Tcp, 1883, requested: true));

    [Theory]
    [InlineData(443)]
    [InlineData(8084)]
    [InlineData(8883)]
    public void Encrypts_IsTrueOnAWebSocketPortWhoseSchemeIsFixedByConvention(int port) =>
        Assert.True(MqttEndpoint.Encrypts(MqttTransport.WebSocket, port, requested: false));

    [Fact]
    public void Encrypts_IsFalseOnAPlainTcpPortNobodyAskedToEncrypt() =>
        Assert.False(MqttEndpoint.Encrypts(MqttTransport.Tcp, 8883, requested: false));

    [Fact]
    public void WebSocketUri_LeavesOffTheSchemesOwnDefaultPort()
    {
        Assert.Equal("wss://broker.invalid", MqttEndpoint.WebSocketUri("broker.invalid", 443, useTls: true));
        Assert.Equal("ws://broker.invalid", MqttEndpoint.WebSocketUri("broker.invalid", 80, useTls: false));
    }

    [Fact]
    public void WebSocketUri_CarriesANonDefaultPort() =>
        Assert.Equal("ws://broker.invalid:9001", MqttEndpoint.WebSocketUri("broker.invalid", 9001, useTls: false));

    [Fact]
    public void WebSocketUri_HonoursAHostTypedWithItsOwnScheme()
    {
        // A broker behind a path is not expressible as host and port, so a typed URI wins outright.
        Assert.Equal("wss://broker.invalid/mqtt",
            MqttEndpoint.WebSocketUri("wss://broker.invalid/mqtt", 9001, useTls: false));
    }

    [Fact]
    public void Reachability_TakesTheSchemesDefaultPortForAWebSocketAuthorityWithoutOne()
    {
        var (host, port) = MqttEndpoint.Reachability("wss://broker.invalid/mqtt", 9001, MqttTransport.WebSocket, true);

        Assert.Equal("broker.invalid", host);
        Assert.Equal(443, port);
    }

    [Fact]
    public void Reachability_ClampsAPortAHandEditPutOutOfRange()
    {
        var (_, port) = MqttEndpoint.Reachability("broker.invalid", 70000, MqttTransport.Tcp, false);

        Assert.Equal(65535, port);
    }

    [Fact]
    public void Resolve_GivesTcpTheHostAndPortAsTyped()
    {
        var address = MqttEndpoint.Resolve(" broker.invalid ", new(1883, MqttTransport.Tcp));

        Assert.Equal(MqttTransport.Tcp, address.Transport);
        Assert.Equal("broker.invalid", address.Host);
        Assert.Equal(1883, address.Port);
        Assert.False(address.Encrypted);
    }

    [Fact]
    public void Resolve_ReportsAWebSocketPortAsEncryptedWhenItsSchemeSaysSo()
    {
        var address = MqttEndpoint.Resolve("broker.invalid", new(443, MqttTransport.WebSocket));

        Assert.Equal("wss://broker.invalid", address.Uri);
        Assert.True(address.Encrypted);
    }
}
