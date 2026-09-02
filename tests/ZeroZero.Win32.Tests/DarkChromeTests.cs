using Xunit;

namespace ZeroZero.Win32.Tests;

public class DarkChromeTests
{
    [Fact]
    public void Apply_FindsBothThemeEntryPointsOnThisWindows()
    {
        // Every Windows this repository builds on has the ordinals, so the call reaching both of
        // them is the measurement; a wrong ordinal is an EntryPointNotFoundException and false.
        Assert.True(DarkChrome.Apply(DarkChromeMode.AllowDark));
        Assert.True(DarkChrome.Apply(DarkChromeMode.Default));
    }
}
