using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>An entity id is a topic segment and a command address at the same time, so two entities
/// sharing one route the first's commands to the second. These pin the alphabet and the collision
/// resolution that keeps that from happening.</summary>
public class MqttEntityIdTests
{
    [Theory]
    [InlineData("CPU load", "cpu_load")]
    [InlineData("Web server (2)", "web_server_2")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("a---b", "a_b")]
    [InlineData("Ærlig-Måke", "rlig_m_ke")]
    public void Normalise_ReducesToTheTopicSafeAlphabet(string raw, string expected) =>
        Assert.Equal(expected, MqttEntityId.Normalise(raw));

    [Fact]
    public void Normalise_CollapsesRunsRatherThanKeepingOneUnderscorePerCharacter()
    {
        // The device id deliberately does not collapse, because it is already carried by every
        // retained topic on every existing installation. An entity id composed at runtime is not.
        Assert.Equal("my_vm", MqttEntityId.Normalise("My VM"));
        Assert.Equal("my_vm", MqttIdentity.Normalise("My VM"));
        Assert.Equal("web_server_2", MqttEntityId.Normalise("Web server (2)"));
        Assert.Equal("web_server__2_", MqttIdentity.Normalise("Web server (2)"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    [InlineData(null)]
    public void Normalise_FallsBackWhenNothingUsableSurvives(string? raw) =>
        Assert.Equal(MqttEntityId.Fallback, MqttEntityId.Normalise(raw));

    [Fact]
    public void Normalise_CapsTheLength()
    {
        string id = MqttEntityId.Normalise(new string('a', MqttEntityId.MaxLength + 20));

        Assert.Equal(MqttEntityId.MaxLength, id.Length);
    }

    [Fact]
    public void Validate_RejectsANameWithNothingUsableInIt()
    {
        Assert.NotNull(MqttEntityId.Validate("!!!"));
        Assert.Null(MqttEntityId.Validate("cpu load"));
    }

    [Fact]
    public void Resolve_GivesTheFirstClaimantThePlainId()
    {
        var ids = MqttEntityId.Resolve(["My VM", "my-vm", "MY  VM"]);

        Assert.Equal(["my_vm", "my_vm_2", "my_vm_3"], ids);
    }

    [Fact]
    public void Resolve_KeepsTheInputOrder()
    {
        // The same list must always produce the same ids: an entity whose id moved between runs
        // would look like a different entity to a receiver.
        Assert.Equal(["b", "a", "b_2"], MqttEntityId.Resolve(["b", "a", "B"]));
    }

    [Fact]
    public void Resolve_LeavesDistinctNamesAlone()
    {
        var ids = MqttEntityId.Resolve(["cpu load", "memory used", "disk free"]);

        Assert.Equal(["cpu_load", "memory_used", "disk_free"], ids);
    }

    [Fact]
    public void Resolve_TrimsTheStemSoASuffixedIdStillFitsTheCap()
    {
        string name = new('a', MqttEntityId.MaxLength);

        var ids = MqttEntityId.Resolve([name, name]);

        Assert.All(ids, id => Assert.True(id.Length <= MqttEntityId.MaxLength));
        Assert.EndsWith("_2", ids[1], StringComparison.Ordinal);
        Assert.NotEqual(ids[0], ids[1]);
    }

    [Fact]
    public void Allocator_HandsOutIdsOneAtATimeAndRemembersThem()
    {
        var allocator = new MqttEntityIdAllocator();

        Assert.Equal("host", allocator.Allocate("Host"));
        Assert.Equal("host_2", allocator.Allocate("host"));
        Assert.True(allocator.Contains("host_2"));
        Assert.False(allocator.Contains("host_3"));
    }

    [Fact]
    public void Allocator_ResolvesACollisionWithAnIdAnEarlierNameAlreadyTook()
    {
        var allocator = new MqttEntityIdAllocator();

        Assert.Equal("vm_2", allocator.Allocate("vm 2"));
        Assert.Equal("vm", allocator.Allocate("vm"));
        // "vm" is taken and "vm_2" was claimed by a different name, so the next free suffix wins.
        Assert.Equal("vm_3", allocator.Allocate("VM"));
    }
}
