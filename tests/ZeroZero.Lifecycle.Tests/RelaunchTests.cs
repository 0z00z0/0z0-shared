using Xunit;
using ZeroZero.Lifecycle;

namespace ZeroZero.Lifecycle.Tests;

public class RelaunchTests
{
    [Fact]
    public void TheArgumentMarksARelaunch()
    {
        Assert.True(Relaunch.WasRelaunched([Relaunch.Argument]));
        Assert.True(Relaunch.WasRelaunched(["--other", Relaunch.Argument]));
    }

    [Fact]
    public void AnythingElseIsALaunch()
    {
        Assert.False(Relaunch.WasRelaunched([]));
        Assert.False(Relaunch.WasRelaunched(["--other"]));
        Assert.False(Relaunch.WasRelaunched([Relaunch.Argument.ToUpperInvariant()]));
    }

    [Fact]
    public void TheArgumentIsWhatTheExitHookPasses() => Assert.Equal("--relaunched", Relaunch.Argument);
}
