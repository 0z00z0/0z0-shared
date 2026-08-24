using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The publish list is declared entirely by the consuming application. What is tested here
/// is the three rules that only bite once entities are discovered at runtime rather than fixed.</summary>
public class MqttPublishRowsTests
{
    private static PublishGroupSet Set(IMqttSettingsStore store, params PublishGroup[] groups) =>
        new(store, groups.Length > 0 ? groups :
        [
            new("state", "State", Info: "What the machine is doing"),
            new("metrics", "Metrics", "Off by default: these cost something to produce",
                DefaultOn: false, Info: "Counters sampled once a minute"),
        ]);

    [Fact]
    public void RowsRenderInDeclarationOrder()
    {
        var rows = MqttPublishRows.Build(Set(new RecordingSettingsStore()));

        Assert.Equal(["state", "metrics"], rows.Select(r => r.Key));
    }

    [Fact]
    public void ADeclaredGroupRendersWhetherOrNotItCurrentlyHasEntities()
    {
        // The module is never told which entities exist, so a row cannot be filtered on having any —
        // rows appearing and vanishing as virtual machines come and go would be unusable.
        var rows = MqttPublishRows.Build(Set(new RecordingSettingsStore(),
            new PublishGroup("machines", "Virtual machines")));

        Assert.Single(rows);
        Assert.Equal("Virtual machines", rows[0].Label);
    }

    [Fact]
    public void StateIsKeyedOnTheGroupKeyNotOnItsPosition()
    {
        var store = new RecordingSettingsStore();
        Set(store).Set("metrics", true);

        // A later version inserts a group ahead of the two that were there.
        var reordered = Set(store,
            new PublishGroup("network", "Network"),
            new PublishGroup("metrics", "Metrics", DefaultOn: false),
            new PublishGroup("state", "State"));
        var rows = MqttPublishRows.Build(reordered);

        Assert.True(rows.Single(r => r.Key == "metrics").On);
        Assert.True(rows.Single(r => r.Key == "state").On);
        Assert.True(rows.Single(r => r.Key == "network").On);
    }

    [Fact]
    public void AGroupNobodyHasTouchedShowsItsOwnDeclaredDefault()
    {
        var rows = MqttPublishRows.Build(Set(new RecordingSettingsStore()));

        Assert.True(rows.Single(r => r.Key == "state").On);
        Assert.False(rows.Single(r => r.Key == "metrics").On);
    }

    [Fact]
    public void TheToggleShowsTheCurrentStateRatherThanTheDeclaredDefault()
    {
        var store = new RecordingSettingsStore();
        var set = Set(store);
        set.Set("state", false);

        var rows = MqttPublishRows.Build(set);

        Assert.False(rows.Single(r => r.Key == "state").On);
        // While the description still justifies the shipped default, which is a different fact.
        Assert.StartsWith("Off by default", rows.Single(r => r.Key == "metrics").Description,
                          StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupCanCarryBothACardDescriptionAndAnInfoLine()
    {
        // One justifies the shipped default, the other says what the group contains. Not redundant.
        var row = MqttPublishRows.Build(Set(new RecordingSettingsStore()))
                                 .Single(r => r.Key == "metrics");

        Assert.True(row.HasDescription);
        Assert.True(row.HasInfo);
        Assert.NotEqual(row.Description, row.Info);
    }

    [Fact]
    public void AGroupWithNoInfoTextGetsNoIcon()
    {
        var rows = MqttPublishRows.Build(Set(new RecordingSettingsStore(),
            new PublishGroup("plain", "Plain"),
            new PublishGroup("blank", "Blank", Info: "   ")));

        Assert.All(rows, r => Assert.False(r.HasInfo));
    }

    [Fact]
    public void AGroupWithNoDescriptionGetsNoDescriptionLine()
    {
        var rows = MqttPublishRows.Build(Set(new RecordingSettingsStore(),
            new PublishGroup("plain", "Plain", Info: "Something")));

        Assert.False(rows[0].HasDescription);
        Assert.True(rows[0].HasInfo);
    }

    [Fact]
    public void AnInfoIconNamesItsOwnGroupToAScreenReader()
    {
        var rows = MqttPublishRows.Build(Set(new RecordingSettingsStore()));

        Assert.Equal("the State group", rows[0].InfoSubject);
        Assert.Equal("the Metrics group", rows[1].InfoSubject);
    }

    [Fact]
    public void StatesReReadsEveryDeclaredGroupByKey()
    {
        var store = new RecordingSettingsStore();
        var set = Set(store);
        set.Set("metrics", true);

        var states = MqttPublishRows.States(set);

        Assert.Equal(["state", "metrics"], states.Keys);
        Assert.True(states["metrics"]);
    }

    [Fact]
    public void RowsAlreadyBuiltDoNotMoveUnderALaterChange()
    {
        var store = new RecordingSettingsStore();
        var set = Set(store);
        var rows = MqttPublishRows.Build(set);

        set.Set("state", false);

        Assert.True(rows.Single(r => r.Key == "state").On);
    }

    /// <summary>A store whose answer moves between calls, so a build that reads it more than once
    /// gives two rows different vintages and a build that reads it once cannot.</summary>
    private sealed class DriftingStore : IMqttSettingsStore
    {
        private readonly MqttSettings _settings = new();

        public int Reads { get; private set; }

        public MqttSettings Read()
        {
            // Everything is on for the first read of a pass and off for every one after it.
            var answer = _settings.Copy();
            if (Reads++ > 0) answer.Groups["second"] = false;
            else answer.Groups["second"] = true;
            answer.Groups["first"] = true;
            return answer;
        }

        public void Update(Action<MqttSettings> mutate) => mutate(_settings);

        // Nothing in this rig subscribes; the interface requires the member, not a raiser.
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    [Fact]
    public void OnePassTakesOneReadSoNoTwoRowsHaveDifferentVintages()
    {
        var store = new DriftingStore();
        var set = new PublishGroupSet(store,
            [new PublishGroup("first", "First"), new PublishGroup("second", "Second")]);

        var rows = MqttPublishRows.Build(set);

        Assert.Equal(1, store.Reads);
        // Reading per row would have taken the second row's answer from a later moment.
        Assert.True(rows.Single(r => r.Key == "second").On);
    }

    [Fact]
    public void ReReadingTheStatesAlsoTakesOneRead()
    {
        var store = new DriftingStore();
        var set = new PublishGroupSet(store,
            [new PublishGroup("first", "First"), new PublishGroup("second", "Second")]);

        var states = MqttPublishRows.States(set);

        Assert.Equal(1, store.Reads);
        Assert.True(states["second"]);
    }
}
