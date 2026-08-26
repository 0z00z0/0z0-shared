namespace ZeroZero.Mqtt;

/// <summary>One rendered row of the publish list: the application's own copy, plus the state the
/// toggle beside it is showing.</summary>
/// <param name="Key">The stored key, and the only thing the state is ever keyed on.</param>
public sealed record MqttPublishRow(
    string Key, string Label, string Description, string Info, bool On)
{
    /// <summary>Whether the row gets an info icon. A group whose author supplied no explanation gets
    /// none, rather than an icon that opens on an empty flyout.</summary>
    public bool HasInfo => !string.IsNullOrWhiteSpace(Info);

    /// <summary>Whether the row gets a description line under its label.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>What a screen reader calls the row's info icon. Several sit on one page, so naming
    /// the subject is what tells them apart.</summary>
    public string InfoSubject => $"the {Label} group";
}

/// <summary>How many declared groups there are and how many are switched on, as a collapsed section
/// summarises itself.</summary>
public readonly record struct MqttPublishTally(int SwitchedOn, int Declared);

/// <summary>The declared publish groups as rows a panel renders. Pure.</summary>
/// <remarks>
/// <para>Three rules live here rather than in the panel, because each is invisible until an entity
/// set becomes dynamic and then is a defect that reads as a redraw. A declared group renders whether
/// or not it currently has entities — rows appearing and vanishing as virtual machines come and go
/// would be unusable. State is read by group key, never by position, so inserting or reordering a
/// group cannot move a user's choices onto different groups. And the order is the declaration order,
/// because the application chose it.</para>
/// <para>The module declares no group of its own and knows the vocabulary of none.</para>
/// </remarks>
public static class MqttPublishRows
{
    /// <summary>One row per declared group, in declaration order, from a single snapshot — so every
    /// row in the pass answers from the same moment.</summary>
    public static IReadOnlyList<MqttPublishRow> Build(PublishGroupSet groups)
    {
        var state = groups.Snapshot();
        return [.. groups.Declared.Select(g =>
            new MqttPublishRow(g.Key, g.Label, g.Description, g.Info, state.IsEnabled(g.Key)))];
    }

    /// <summary>The rows again with fresh state and the same copy. What a panel calls to re-read the
    /// toggles without rebuilding the controls under them.</summary>
    public static IReadOnlyDictionary<string, bool> States(PublishGroupSet groups)
    {
        var state = groups.Snapshot();
        return groups.Declared.ToDictionary(g => g.Key, g => state.IsEnabled(g.Key), StringComparer.Ordinal);
    }

    /// <summary>The declared groups counted, and how many of them are on — what a collapsed section
    /// says about itself. From one snapshot, so the two numbers describe the same moment.</summary>
    public static MqttPublishTally Tally(PublishGroupSet groups)
    {
        var state = groups.Snapshot();
        return new(groups.Declared.Count(g => state.IsEnabled(g.Key)), groups.Declared.Count);
    }
}
