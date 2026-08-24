using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The device id is the client id, the topic segment and the <c>unique_id</c> stem, so a
/// change to how it is derived orphans every retained topic the old one owned.</summary>
public class MqttIdentityTests
{
    [Fact]
    public void Default_JoinsTheTopicRootToTheSanitisedMachineName() =>
        Assert.Equal("exampleapp_desk_01", MqttIdentity.Default("exampleapp", "DESK-01"));

    [Fact]
    public void Default_FallsBackWhenTheMachineNameSanitisesToNothing() =>
        Assert.Equal("exampleapp_device", MqttIdentity.Default("exampleapp", "---"));

    [Fact]
    public void Normalise_CapsTheLength() =>
        Assert.Equal(MqttIdentity.MaxLength,
            MqttIdentity.Normalise(new string('x', MqttIdentity.MaxLength + 5)).Length);

    [Fact]
    public void Normalise_DoesNotForceTheTopicRootPrefix() =>
        Assert.Equal("desk", MqttIdentity.Normalise("Desk"));

    [Fact]
    public void Validate_AcceptsBlankBecauseItMeansUseTheDefault() =>
        Assert.Null(MqttIdentity.Validate("   "));

    [Fact]
    public void Validate_RejectsAnIdWithNoLetterOrDigit() =>
        Assert.NotNull(MqttIdentity.Validate("!!!"));

    [Fact]
    public void Validate_RejectsAnIdPastTheCap() =>
        Assert.NotNull(MqttIdentity.Validate(new string('x', MqttIdentity.MaxLength + 1)));

    [Fact]
    public void Effective_PrefersTheCustomValue() =>
        Assert.Equal("workshop", MqttIdentity.Effective("Workshop", "exampleapp", "DESK-01"));

    [Fact]
    public void Effective_FallsBackWhenACustomValueSanitisesToNothingUsable()
    {
        // Reachable by hand-editing the settings file past the validator.
        Assert.Equal("exampleapp_desk_01", MqttIdentity.Effective("###", "exampleapp", "DESK-01"));
    }
}
