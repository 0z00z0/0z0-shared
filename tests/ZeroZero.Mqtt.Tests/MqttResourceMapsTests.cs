using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The precedence between the maps a merged index holds a module string in. The shape is
/// the one a consuming application's index has: its own entries under the bare map name, the
/// library's under the library's name.</summary>
public class MqttResourceMapsTests
{
    private const string Library = "ZeroZero.Mqtt.WinUI";
    private const string Map = "Resources";

    private static readonly Dictionary<string, Dictionary<string, string>> MergedIndex =
        new(StringComparer.Ordinal)
        {
            [Map] = new(StringComparer.Ordinal) { ["ButtonApply"] = "HOST-OVERRIDE" },
            [$"{Library}/{Map}"] = new(StringComparer.Ordinal)
            {
                ["ButtonApply"] = "Apply",
                ["ButtonTest"] = "Test connection",
            },
        };

    /// <summary>What the loader does: the first map, in order, that answers the key.</summary>
    private static string? First(string key) =>
        MqttResourceMaps.Subtrees(Library, Map)
            .Select(path => MergedIndex.GetValueOrDefault(path)?.GetValueOrDefault(key))
            .FirstOrDefault(value => value is not null);

    [Fact]
    public void AHostsOwnEntryOutranksTheLibrarysCopy() =>
        Assert.Equal("HOST-OVERRIDE", First("ButtonApply"));

    [Fact]
    public void AKeyTheHostDoesNotSupplyFallsToTheLibrarysMap() =>
        Assert.Equal("Test connection", First("ButtonTest"));
}
