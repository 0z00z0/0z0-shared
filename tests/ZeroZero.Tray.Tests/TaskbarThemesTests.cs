using Microsoft.Win32;
using Xunit;

namespace ZeroZero.Tray.Tests;

public class TaskbarThemesTests
{
    [Fact]
    public void FromRegistryValue_OneIsLight()
    {
        Assert.Equal(TaskbarTheme.Light, TaskbarThemes.FromRegistryValue(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void FromRegistryValue_AnyOtherDwordIsDark(int value)
    {
        Assert.Equal(TaskbarTheme.Dark, TaskbarThemes.FromRegistryValue(value));
    }

    [Fact]
    public void FromRegistryValue_AbsentIsDark()
    {
        Assert.Equal(TaskbarTheme.Dark, TaskbarThemes.FromRegistryValue(null));
    }

    [Fact]
    public void FromRegistryValue_AValueOfAnotherKindIsDark()
    {
        // A string "1" or a QWORD 1 is not the DWORD Windows writes; the reader takes no guess.
        Assert.Equal(TaskbarTheme.Dark, TaskbarThemes.FromRegistryValue("1"));
        Assert.Equal(TaskbarTheme.Dark, TaskbarThemes.FromRegistryValue(1L));
    }

    [Fact]
    public void Read_MatchesTheSystemThemeValueReadDirectly()
    {
        // The same value through the flat registry API, mapped the way Windows documents it: the
        // taskbar is light exactly when SystemUsesLightTheme is 1.
        object? value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", null);
        var expected = value is int and 1 ? TaskbarTheme.Light : TaskbarTheme.Dark;

        Assert.Equal(expected, TaskbarThemes.Read());
    }

    [Fact]
    public void StrokeToneFor_IsTheOppositeOfTheTaskbar()
    {
        Assert.Equal(StrokeTone.Dark, TaskbarThemes.StrokeToneFor(TaskbarTheme.Light));
        Assert.Equal(StrokeTone.Light, TaskbarThemes.StrokeToneFor(TaskbarTheme.Dark));
    }
}
