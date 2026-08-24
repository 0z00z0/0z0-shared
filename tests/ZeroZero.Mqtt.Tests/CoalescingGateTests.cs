using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>A burst of change signals must cost one pass plus at most one trailing pass. The
/// trailing pass is what guarantees the last snapshot wins.</summary>
public class CoalescingGateTests
{
    [Fact]
    public void TheFirstSignalStartsTheLoop() => Assert.True(new CoalescingGate().Signal());

    [Fact]
    public void ASignalArrivingWhileOneRunsStartsNothingSecond()
    {
        var gate = new CoalescingGate();
        gate.Signal();

        Assert.False(gate.Signal());
    }

    [Fact]
    public void ASignalArrivingDuringAPassArmsATrailingOne()
    {
        var gate = new CoalescingGate();
        gate.Signal();
        gate.BeginPass();

        gate.Signal();

        Assert.True(gate.ShouldRepeat());
    }

    [Fact]
    public void ASignalArrivingBeforeThePassStartsIsCoveredByIt()
    {
        var gate = new CoalescingGate();
        gate.Signal();
        gate.Signal();
        gate.BeginPass();

        Assert.False(gate.ShouldRepeat());
    }

    [Fact]
    public void TheLoopEndsAndTheNextSignalStartsAFreshOne()
    {
        var gate = new CoalescingGate();
        gate.Signal();
        gate.BeginPass();
        gate.ShouldRepeat();

        Assert.True(gate.Signal());
    }

    [Fact]
    public void ABurstOfSignalsCostsOnePassPlusOneTrailingPass()
    {
        var gate = new CoalescingGate();
        int passes = 0;

        Assert.True(gate.Signal());
        do
        {
            gate.BeginPass();
            passes++;
            if (passes == 1) for (int i = 0; i < 20; i++) gate.Signal();
        }
        while (gate.ShouldRepeat());

        Assert.Equal(2, passes);
    }
}
