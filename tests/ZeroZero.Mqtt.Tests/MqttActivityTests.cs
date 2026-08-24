using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>An instance owned by one connection, never a static: a consumer detects a rebuilt
/// connection by identity, and a process-wide slot would leave one connection reporting the other's
/// traffic.</summary>
public class MqttActivityTests
{
    [Fact]
    public void NothingIsRecordedUntilSomethingHappens()
    {
        var activity = new MqttActivity();

        Assert.Null(activity.LastPublish);
        Assert.Null(activity.LastCommand);
    }

    [Fact]
    public void TwoActivitiesDoNotSeeEachOthersTraffic()
    {
        var first = new MqttActivity();
        var second = new MqttActivity();

        first.RecordPublish();
        first.RecordCommand("quiet_mode");

        Assert.Null(second.LastPublish);
        Assert.Null(second.LastCommand);
    }

    [Fact]
    public void APublishIsRecordedAtTheInstantGiven()
    {
        var when = new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero);
        var activity = new MqttActivity();

        activity.RecordPublish(when);

        Assert.Equal(when, activity.LastPublish);
    }

    [Fact]
    public void ACommandIsSwappedAsAWholeRecord()
    {
        // A reader must never pair one command's timestamp with another's entity.
        var activity = new MqttActivity();
        var first = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);
        var second = first.AddMinutes(5);

        activity.RecordCommand("quiet_mode", first);
        activity.RecordCommand("profile", second);

        Assert.Equal(new MqttCommandRecord(second, "profile"), activity.LastCommand);
    }

    [Fact]
    public void EveryInstantItRecordsIsOffsetAware()
    {
        var activity = new MqttActivity();
        activity.RecordPublish();
        activity.RecordCommand("quiet_mode");

        Assert.Equal(TimeSpan.Zero, activity.LastPublish!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, activity.LastCommand!.When.Offset);
    }
}
