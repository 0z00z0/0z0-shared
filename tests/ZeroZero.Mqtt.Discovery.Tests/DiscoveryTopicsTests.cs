using Xunit;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>Every topic the layer touches is composed in one place, so what is published and what is
/// emptied cannot drift apart.</summary>
public class DiscoveryTopicsTests
{
    [Fact]
    public void TheDeviceDocumentSitsUnderTheDeviceSegment() =>
        Assert.Equal(
            "homeassistant/device/exampleapp_desk01/config",
            DiscoveryTopics.Device(Sample.Prefix, Sample.DeviceId));

    [Fact]
    public void APerComponentConfigCarriesItsComponentAndItsEntityId() =>
        Assert.Equal(
            "homeassistant/sensor/exampleapp_desk01/cpu_load/config",
            DiscoveryTopics.Component(Sample.Prefix, "sensor", Sample.DeviceId, "cpu_load"));

    [Fact]
    public void TheBirthTopicIsTheReceiversOwn() =>
        Assert.Equal("homeassistant/status", DiscoveryTopics.Status(Sample.Prefix));

    [Fact]
    public void APrefixOfItsOwnMovesEveryTopicWithIt()
    {
        Assert.Equal("ha/device/exampleapp_desk01/config", DiscoveryTopics.Device("ha", Sample.DeviceId));
        Assert.Equal("ha/status", DiscoveryTopics.Status("ha"));
    }
}
