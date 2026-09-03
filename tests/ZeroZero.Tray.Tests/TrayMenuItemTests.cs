using Xunit;
using ZeroZero.Tray.WinUI;

namespace ZeroZero.Tray.Tests;

/// <summary>
/// The menu descriptor as an application writes it. The three factories are what the tray guide
/// lists, and they are not alike: a command and a toggle carry an enabled flag, a separator carries
/// nothing at all — it is a rule between groups, and there is nothing about it to enable.
/// </summary>
public sealed class TrayMenuItemTests
{
    [Fact]
    public void ACommandIsEnabledUnlessTheApplicationSaysOtherwise()
    {
        var enabled = TrayMenuItem.Command("Settings", () => { });
        var disabled = TrayMenuItem.Command("Settings", () => { }, isEnabled: false);

        Assert.True(enabled.IsEnabled);
        Assert.False(disabled.IsEnabled);
        Assert.Null(enabled.IsChecked);
        Assert.False(enabled.IsSeparator);
        Assert.Equal("Settings", enabled.Text);
    }

    [Fact]
    public void AToggleCarriesItsCheckMarkAndTheSameEnabledFlag()
    {
        var on = TrayMenuItem.Toggle("Run at logon", isChecked: true, () => { });
        var off = TrayMenuItem.Toggle("Run at logon", isChecked: false, () => { }, isEnabled: false);

        Assert.True(on.IsChecked);
        Assert.True(on.IsEnabled);
        Assert.False(off.IsChecked);
        Assert.False(off.IsEnabled);
    }

    /// <summary>The one the guide got wrong: a separator has no enabled flag to give it.</summary>
    [Fact]
    public void ASeparatorTakesNothingAndIsNotSomethingToEnable()
    {
        var separator = TrayMenuItem.Separator();

        Assert.True(separator.IsSeparator);
        Assert.False(separator.IsEnabled);
        Assert.Null(separator.Text);
        Assert.Null(separator.Invoke);
    }

    [Fact]
    public void AnEntryWithNoLabelIsRefused()
    {
        Assert.Throws<ArgumentException>(() => TrayMenuItem.Command(" ", () => { }));
        Assert.Throws<ArgumentException>(() => TrayMenuItem.Toggle(" ", isChecked: true, () => { }));
    }
}
