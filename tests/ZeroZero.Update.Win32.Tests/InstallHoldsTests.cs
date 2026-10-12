using Xunit;

namespace ZeroZero.Update.Win32.Tests;

public class InstallHoldsTests
{
    private readonly InstallHolds _holds = new();
    private readonly RecordingLogSink _log = new();

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void AHold_LastsUntilDisposed_AndDisposingTwiceIsHarmless()
    {
        IDisposable hold = _holds.Hold("a focus session is running");

        Assert.Equal(InstallMoment.NotNow("a focus session is running"), _holds.Ask(_log));

        hold.Dispose();
        hold.Dispose();

        Assert.Equal(InstallMoment.Now, _holds.Ask(_log));
    }

    [Fact]
    public void FunctionHolds_AreReadAtEachAsk_AndTheFirstRegisteredWins()
    {
        bool lidWait = false;
        _holds.Hold(() => lidWait ? "waiting for the lid" : null);
        _holds.Hold("a focus session is running");

        Assert.Equal("a focus session is running", _holds.Ask(_log).Reason);

        lidWait = true;
        Assert.Equal("waiting for the lid", _holds.Ask(_log).Reason);
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public void AFunctionThatThrows_Holds_AndIsLoggedOnce()
    {
        _holds.Hold(() => throw new InvalidOperationException("the check broke"));

        Assert.Equal(InstallMoment.NotNow(InstallHolds.Unreadable), _holds.Ask(_log));
        Assert.Equal(InstallMoment.NotNow(InstallHolds.Unreadable), _holds.Ask(_log));
        Assert.Single(_log.Errors);
    }

    [Fact]
    public void HoldsTakenAndReleasedOnManyThreads_LeaveNothingHeld()
    {
        Parallel.For(0, 2000, i =>
        {
            using IDisposable hold = i % 2 == 0 ? _holds.Hold("a job is running") : _holds.Hold(() => "a job is running");
            Assert.False(_holds.Ask(_log).Accepted);
        });

        Assert.Equal(InstallMoment.Now, _holds.Ask(_log));
    }
}
