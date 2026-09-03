using Xunit;
using ZeroZero.Tray.WinUI;

namespace ZeroZero.Tray.Tests;

public class TrayClickPolicyTests
{
    private static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(500);

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void ALeftUpIsALeftClick()
    {
        var policy = new TrayClickPolicy(DoubleClickTime);

        Assert.Equal(TrayClick.Left, policy.OnLeftUp(At(1000)));
    }

    [Fact]
    public void ADoubleClickWithinTheDoubleClickTimeOfAnAcceptedLeftIsADouble()
    {
        var policy = new TrayClickPolicy(DoubleClickTime);
        policy.OnLeftUp(At(1000));

        Assert.Equal(TrayClick.Double, policy.OnDoubleClick(At(1500)));
    }

    [Fact]
    public void ADoubleClickAfterTheDoubleClickTimeIsIgnored()
    {
        var policy = new TrayClickPolicy(DoubleClickTime);
        policy.OnLeftUp(At(1000));

        Assert.Equal(TrayClick.Ignored, policy.OnDoubleClick(At(1501)));
    }

    [Fact]
    public void ADoubleClickWithNoLeftBeforeItIsIgnored()
    {
        var policy = new TrayClickPolicy(DoubleClickTime);

        Assert.Equal(TrayClick.Ignored, policy.OnDoubleClick(At(1000)));
    }

    [Fact]
    public void ADoubleIsReportedOnceForOneLeft()
    {
        var policy = new TrayClickPolicy(DoubleClickTime);
        policy.OnLeftUp(At(1000));
        policy.OnDoubleClick(At(1200));

        Assert.Equal(TrayClick.Ignored, policy.OnDoubleClick(At(1300)));
    }

    [Fact]
    public void ALeftUpWithinTheGuardAfterADismissalIsIgnoredAndTheNextOneIsNot()
    {
        var policy = new TrayClickPolicy(DoubleClickTime);
        policy.NoteDismissed(At(1000));

        Assert.Equal(TrayClick.Ignored, policy.OnLeftUp(At(1100)));
        Assert.Equal(TrayClick.Left, policy.OnLeftUp(At(1200)));
    }

    [Fact]
    public void ALeftUpOnceTheGuardHasElapsedIsALeftClick()
    {
        var policy = new TrayClickPolicy(DoubleClickTime);
        policy.NoteDismissed(At(1000));

        Assert.Equal(TrayClick.Left, policy.OnLeftUp(At(1500)));
    }

    [Fact]
    public void ADoubleClickWhoseFirstHalfWasGuardedAwayIsIgnored()
    {
        var policy = new TrayClickPolicy(DoubleClickTime);
        policy.NoteDismissed(At(1000));
        policy.OnLeftUp(At(1100));

        Assert.Equal(TrayClick.Ignored, policy.OnDoubleClick(At(1300)));
    }

    [Fact]
    public void AGuardOfItsOwnLengthIsHonoured()
    {
        var policy = new TrayClickPolicy(DoubleClickTime, reopenGuard: TimeSpan.FromSeconds(2));
        policy.NoteDismissed(At(1000));

        Assert.Equal(TrayClick.Ignored, policy.OnLeftUp(At(2500)));
    }

    [Fact]
    public void ADoubleClickTimeThatIsNotPositiveIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrayClickPolicy(TimeSpan.Zero));
    }
}
