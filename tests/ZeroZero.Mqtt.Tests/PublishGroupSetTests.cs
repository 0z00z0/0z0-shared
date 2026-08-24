using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The declared groups and their state. A publish pass takes one snapshot, so it cannot
/// announce one entity under the old answer and the next under the new.</summary>
public class PublishGroupSetTests
{
    private static PublishGroupSet Set(IMqttSettingsStore store) => new(store, [
        new("state", "State", "What the machine is doing"),
        new("metrics", "Metrics", "Figures that cost something to produce", DefaultOn: false),
    ]);

    [Fact]
    public void Declared_IsTheApplicationsVocabularyInDeclarationOrder()
    {
        var groups = Set(new RecordingSettingsStore()).Declared;

        Assert.Equal(["state", "metrics"], groups.Select(g => g.Key));
        Assert.Equal("Figures that cost something to produce", groups[1].Description);
    }

    [Fact]
    public void AGroupNobodyHasTouchedTakesItsOwnDeclaredDefault()
    {
        var set = Set(new RecordingSettingsStore());

        Assert.True(set.IsEnabled("state"));
        Assert.False(set.IsEnabled("metrics"));
    }

    [Fact]
    public void NoGroupMeansAlwaysPublished()
    {
        var set = Set(new RecordingSettingsStore());

        Assert.True(set.IsEnabled(null));
        Assert.True(set.IsEnabled(""));
    }

    [Fact]
    public void AGroupKeyNothingDeclaredIsOn()
    {
        // An entity carrying an unknown group key must not vanish silently.
        Assert.True(Set(new RecordingSettingsStore()).IsEnabled("typo"));
    }

    [Fact]
    public void AStoredStateOutranksTheDeclaredDefault()
    {
        var store = new RecordingSettingsStore();
        var set = Set(store);

        set.Set("metrics", true);
        set.Set("state", false);

        Assert.True(set.IsEnabled("metrics"));
        Assert.False(set.IsEnabled("state"));
    }

    [Fact]
    public void ASnapshotDoesNotMoveUnderTheCallerHoldingIt()
    {
        var store = new RecordingSettingsStore();
        var set = Set(store);
        var snapshot = set.Snapshot();

        set.Set("state", false);

        Assert.True(snapshot.IsEnabled("state"));
        Assert.False(set.Snapshot().IsEnabled("state"));
    }

    [Fact]
    public void SeveralGroupsMovingTogetherCostOneWriteAndOneNotification()
    {
        var store = new RecordingSettingsStore();
        var set = Set(store);
        int notifications = 0;
        set.Changed += () => notifications++;

        set.Set([new("state", false), new("metrics", true)]);

        Assert.Equal(1, store.Writes);
        Assert.Equal(1, notifications);
        Assert.False(set.IsEnabled("state"));
        Assert.True(set.IsEnabled("metrics"));
    }

    [Fact]
    public void SettingNothingWritesNothing()
    {
        var store = new RecordingSettingsStore();

        Set(store).Set([]);

        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void AToggleRaisesTheGroupsOwnEventAsWellAsTheStoresChange()
    {
        // The two mean different things: the group event means republish, the store's means the
        // broker settings may have moved.
        var store = new RecordingSettingsStore();
        var set = Set(store);
        bool group = false, stored = false;
        set.Changed += () => group = true;
        store.Changed += () => stored = true;

        set.Set("state", false);

        Assert.True(group);
        Assert.True(stored);
    }
}
