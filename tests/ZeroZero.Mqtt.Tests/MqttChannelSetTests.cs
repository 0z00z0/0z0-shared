using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The dedupe cache and the declared set. A dedupe slot recording a payload that never
/// reached the broker is what leaves a topic wrong until the value happens to change again, so the
/// rollback and the compare-and-set are the load-bearing parts.</summary>
public class MqttChannelSetTests
{
    private static MqttChannel Channel(string key, string? payload = "value") => new(key, () => payload);

    [Fact]
    public void Accept_TakesAChangedPayloadAndDropsAnUnchangedOne()
    {
        var set = new MqttChannelSet([Channel("cpu_load")]);

        Assert.True(set.Accept("cpu_load", "42"));
        Assert.False(set.Accept("cpu_load", "42"));
        Assert.True(set.Accept("cpu_load", "43"));
    }

    [Fact]
    public void Force_TakesTheSlotWhetherOrNotThePayloadMoved()
    {
        var set = new MqttChannelSet([Channel("cpu_load")]);
        set.Accept("cpu_load", "42");

        set.Force("cpu_load", "42");

        Assert.Equal("42", set.LastPayload("cpu_load"));
    }

    [Fact]
    public void ForgetOne_MakesTheNextPassResendThatChannelAndOnlyThat()
    {
        var set = new MqttChannelSet([Channel("cpu_load"), Channel("quiet_mode")]);
        set.Accept("cpu_load", "42");
        set.Accept("quiet_mode", "ON");

        set.Forget("cpu_load");

        Assert.True(set.Accept("cpu_load", "42"));
        Assert.False(set.Accept("quiet_mode", "ON"));
    }

    [Fact]
    public void ForgetAll_MakesTheNextPassResendEverything()
    {
        var set = new MqttChannelSet([Channel("cpu_load"), Channel("quiet_mode")]);
        set.Accept("cpu_load", "42");
        set.Accept("quiet_mode", "ON");

        set.Forget();

        Assert.True(set.Accept("cpu_load", "42"));
        Assert.True(set.Accept("quiet_mode", "ON"));
    }

    [Fact]
    public void HasPublished_IsFalseUntilSomethingTakesTheSlot()
    {
        var set = new MqttChannelSet([Channel("cpu_load")]);

        Assert.False(set.HasPublished("cpu_load"));
        set.Accept("cpu_load", "42");
        Assert.True(set.HasPublished("cpu_load"));
    }

    [Fact]
    public void Replace_ReportsTheChannelsThatHaveGoneAndTheOnesThatHaveArrived()
    {
        // Both halves are acted on: the ones that have gone leave a retained value to empty, and the
        // ones that have arrived have nothing on their topic until something asks them for a value.
        var set = new MqttChannelSet([Channel("cpu_load"), Channel("quiet_mode")]);

        var (departed, arrived) = set.Replace([Channel("cpu_load"), Channel("gpu_load")]);

        Assert.Equal(["quiet_mode"], departed);
        Assert.Equal(["gpu_load"], arrived);
    }

    [Fact]
    public void Replace_ReportsNothingArrivedForAnUnchangedSet()
    {
        var set = new MqttChannelSet([Channel("cpu_load")]);

        var (departed, arrived) = set.Replace([Channel("cpu_load")]);

        Assert.Empty(departed);
        Assert.Empty(arrived);
    }

    [Fact]
    public void Replace_KeepsTheDedupeEntriesOfTheChannelsThatSurvive()
    {
        // A rebuilt set must not re-send every unchanged payload: at a few dozen entities that is a
        // few dozen round trips for a change that touched one of them.
        var set = new MqttChannelSet([Channel("cpu_load"), Channel("quiet_mode")]);
        set.Accept("cpu_load", "42");

        set.Replace([Channel("cpu_load")]);

        Assert.False(set.Accept("cpu_load", "42"));
    }

    [Fact]
    public void Replace_DropsTheDedupeEntryOfAChannelThatHasGone()
    {
        var set = new MqttChannelSet([Channel("quiet_mode")]);
        set.Accept("quiet_mode", "ON");

        set.Replace([]);
        set.Replace([Channel("quiet_mode")]);

        Assert.True(set.Accept("quiet_mode", "ON"));
    }

    [Fact]
    public void AChannelMayNotTakeTheAvailabilityTopic()
    {
        // The will and the state would then contend for one topic.
        Assert.Throws<ArgumentException>(() => new MqttChannelSet([Channel(MqttTopics.AvailabilityKey)]));
    }

    [Fact]
    public void TwoChannelsMayNotShareOneKey()
    {
        // A dictionary would keep the last silently, and one entity's value would appear under
        // another's topic.
        Assert.Throws<ArgumentException>(() => new MqttChannelSet([Channel("cpu_load"), Channel("cpu_load")]));
    }

    [Fact]
    public void AKeyCarryingASeparatorIsRejected() =>
        Assert.Throws<ArgumentException>(() => new MqttChannelSet([Channel("cpu/load")]));

    [Fact]
    public void Signal_StartsOneLoopAndArmsATrailingPassForTheRest()
    {
        var set = new MqttChannelSet([Channel("cpu_load")]);

        Assert.True(set.Signal("cpu_load"));
        Assert.False(set.Signal("cpu_load"));

        set.BeginPass("cpu_load");
        Assert.False(set.ShouldRepeat("cpu_load"));
    }

    [Fact]
    public void Signal_ForAnUndeclaredChannelStartsNothing() =>
        Assert.False(new MqttChannelSet([]).Signal("nothing_declared"));
}
