using Xunit;

namespace ZeroZero.Tray.Tests;

/// <summary>
/// The decision of what to write into the shell's per-icon settings. The settings are the person's
/// own, one entry per icon the shell has ever seen, so what is pinned here is the rule that stops a
/// write landing where it was never meant to: one entry, found by the icon's identity, and nothing
/// at all wherever that entry is already as wanted, missing, or not the only one.
/// </summary>
public class TrayIconPlacementsTests
{
    private static readonly Guid Ours = new("5D1F3B62-7A4C-4E0B-9C2D-A1B2C3D4E5F6");
    private static readonly Guid Theirs = new("0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0");

    [Fact]
    public void TheEntryCarryingTheIconIsTheOneWritten()
    {
        TrayIconSetting[] settings =
        [
            new("11", Theirs, TrayIconPlacement.Overflow),
            new("22", Ours, TrayIconPlacement.Overflow),
            new("33", Theirs, TrayIconPlacement.NotificationArea),
        ];

        var plan = TrayIconPlacements.Plan(settings, Ours, TrayIconPlacement.NotificationArea);

        Assert.Equal(new TrayIconPlacementPlan("22", 1), plan);
    }

    [Fact]
    public void AskingForTheOverflowWritesTheOffValueIntoThatSameEntry()
    {
        TrayIconSetting[] settings = [new("22", Ours, TrayIconPlacement.NotificationArea)];

        var plan = TrayIconPlacements.Plan(settings, Ours, TrayIconPlacement.Overflow);

        Assert.Equal(new TrayIconPlacementPlan("22", 0), plan);
    }

    [Fact]
    public void AnEntryAlreadyWhereItIsWantedIsLeftAlone()
    {
        TrayIconSetting[] settings = [new("22", Ours, TrayIconPlacement.NotificationArea)];

        Assert.Null(TrayIconPlacements.Plan(settings, Ours, TrayIconPlacement.NotificationArea));
    }

    [Fact]
    public void NothingIsWrittenWhileTheShellKeepsNoEntryForTheIcon()
    {
        // The entry appears the first time the shell sees the icon, so an application asking
        // before that has nothing to write into and must not invent one.
        TrayIconSetting[] settings = [new("11", Theirs, TrayIconPlacement.Overflow)];

        Assert.Null(TrayIconPlacements.Plan(settings, Ours, TrayIconPlacement.NotificationArea));
    }

    [Fact]
    public void NothingIsWrittenWhenMoreThanOneEntryCarriesTheIcon()
    {
        // Two installations of one application. Neither entry is this process's to choose between,
        // and writing both would move an icon nobody asked about.
        TrayIconSetting[] settings =
        [
            new("22", Ours, TrayIconPlacement.Overflow),
            new("44", Ours, TrayIconPlacement.Overflow),
        ];

        Assert.Null(TrayIconPlacements.Plan(settings, Ours, TrayIconPlacement.NotificationArea));
    }

    [Fact]
    public void TheOverflowIsWhatEveryValueButOneMeans()
    {
        Assert.Equal(TrayIconPlacement.NotificationArea, TrayIconPlacements.FromRegistryValue(1));
        Assert.Equal(TrayIconPlacement.Overflow, TrayIconPlacements.FromRegistryValue(0));
        Assert.Equal(TrayIconPlacement.Overflow, TrayIconPlacements.FromRegistryValue(null));
        Assert.Equal(TrayIconPlacement.Overflow, TrayIconPlacements.FromRegistryValue("1"));
    }
}
