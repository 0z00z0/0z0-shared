namespace ZeroZero.Mqtt.Discovery;

/// <summary>A choice from a list the receiver can also write.</summary>
/// <remarks>
/// <para>The option list is a function, not a fixed array: a list composed from what the machine
/// currently holds changes while the application runs, and a changed list is a changed announcement.
/// It is read on every pass.</para>
/// <para>A select is the one component that never publishes an empty payload. A receiver ignores one
/// on a select and goes on offering the last option it saw, so "no reading" has to arrive as a value
/// — which means it has to be an option. <see cref="NoOption"/> is that value, and it is written into
/// every published option list because a reading can go absent at any moment between two
/// announcements.</para>
/// </remarks>
public sealed class MqttSelect : MqttCommandEntity
{
    /// <summary>The sentinel a select falls back to, and the only option it offers when the list is
    /// otherwise empty — a receiver rejects a select with no options at all.</summary>
    public const string DefaultNoOption = "(none)";

    public override string Platform => "select";

    public override string? NoValuePayload => NoOption;

    /// <summary>The options as they stand. Read on every announcement pass.</summary>
    public required Func<IReadOnlyList<string>> Options { get; init; }

    /// <summary>The option currently in force, or null when there is none.</summary>
    public required Func<string?> Read { get; init; }

    /// <summary>What to do with an inbound option that is one of the current ones.</summary>
    public required Func<string, MqttCommandVerdict> Apply { get; init; }

    /// <summary>What is published when there is no current option, and what the picker shows for it.</summary>
    public string NoOption { get; init; } = DefaultNoOption;

    /// <summary>The list as published: the current options, and the sentinel that stands for none.</summary>
    public IReadOnlyList<string> PublishedOptions()
    {
        var declared = Options();
        return declared.Contains(NoOption, StringComparer.Ordinal) ? declared : [.. declared, NoOption];
    }

    public override MqttCommandVerdict Accept(string payload)
    {
        // The sentinel is offered so the topic can always carry a value, not so it can be asked for:
        // "no option" is a reading, and there is nothing to apply.
        if (string.Equals(payload, NoOption, StringComparison.Ordinal))
            return MqttCommandVerdict.NotAnOption($"'{NoOption}' stands for no current value.");

        return Options().Contains(payload, StringComparer.Ordinal)
            ? Apply(payload)
            : MqttCommandVerdict.NotAnOption($"'{payload}' is not one of the current options.");
    }

    internal override string? Validate() =>
        NoOption.Length == 0
            ? $"Entity '{EntityId}' declares an empty sentinel, which is the payload a select may never carry."
            : base.Validate();

    private protected override string? ReadPayload() => Read();

    internal override void Describe(DiscoveryKeys keys) => keys.SetList("options", PublishedOptions());
}
