using System.Diagnostics;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>What the reconnect delay does across a sequence of events, and what the wait it drives
/// does when the process is shutting down. None of it runs until a network drops, so a working build
/// says nothing about it: a broken floor hammers the broker, a broken reset leaves a healthy link
/// reconnecting a minute late for the rest of the session, and a flap that reads as a success
/// removes the backoff altogether.</summary>
/// <remarks>Driven through <see cref="MqttReconnectBackoff"/>, which is handed the instant rather
/// than reading a clock, so a sequence that spans minutes is asserted in microseconds. Only the
/// three tests that are about the wait itself spend real time, and they spend under a second each.
/// </remarks>
public class MqttReconnectBackoffTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);

    private static MqttConnection Unconfigured() =>
        new(new MqttConnectionSetup { TopicRoot = "exampleapp" });

    /// <summary>What a broker that is simply down earns: each round waits twice the last, and the
    /// doubling stops at the cap rather than running past it.</summary>
    [Fact]
    public void EachFailedRoundDoublesTheWaitUntilItReachesTheCap()
    {
        var backoff = new MqttReconnectBackoff();
        var waits = new List<TimeSpan>();

        for (int round = 0; round < 7; round++)
        {
            backoff.Failed();
            waits.Add(backoff.Delay);
        }

        TimeSpan[] ladder =
            [Seconds(6), Seconds(12), Seconds(24), Seconds(48), Seconds(60), Seconds(60), Seconds(60)];
        Assert.Equal(ladder, waits);
    }

    /// <summary>The whole point of the mechanism: a peer that accepts the connection and drops it
    /// again inside the flap window must not have each connect read as a success. Every flap costs
    /// more than the last, so a flapping broker cannot pull the retry rate back to continuous.</summary>
    [Fact]
    public void APeerThatKeepsDroppingInsideTheFlapWindowEscalatesInsteadOfResetting()
    {
        var backoff = new MqttReconnectBackoff();
        var now = Start;
        var waits = new List<TimeSpan>();

        for (int flap = 0; flap < 6; flap++)
        {
            backoff.Connected(now);
            now += Seconds(5);                    // the session dies young

            var wait = backoff.BeforeConnect(now);
            Assert.NotNull(wait);                 // a flap is never retried at once
            waits.Add(wait.Value);
            now += wait.Value;                    // which the loop then waits out
        }

        TimeSpan[] ladder =
            [Seconds(6), Seconds(12), Seconds(24), Seconds(48), Seconds(60), Seconds(60)];
        Assert.Equal(ladder, waits);
    }

    /// <summary>A drop of a session that lasted is the network, not a flap, so it reconnects at once
    /// and is charged nothing.</summary>
    [Fact]
    public void ASessionThatOutlivedTheFlapWindowReconnectsAtOnceAndCostsNothing()
    {
        var backoff = new MqttReconnectBackoff();
        backoff.Connected(Start);

        Assert.Null(backoff.BeforeConnect(Start + Seconds(45)));

        backoff.Failed();
        Assert.Equal(Seconds(6), backoff.Delay);   // the ladder starts where it always does
    }

    /// <summary>A blip that pushed the wait most of the way to the cap must not still be charged once
    /// the connection has held. Without the reset one bad minute leaves a healthy link reconnecting a
    /// minute late for the rest of the session.</summary>
    [Fact]
    public void AConnectionThatHoldsPastTheFlapWindowDropsBackToTheFloor()
    {
        var backoff = new MqttReconnectBackoff();
        for (int round = 0; round < 4; round++) backoff.Failed();
        Assert.Equal(Seconds(48), backoff.Delay);

        backoff.Connected(Start);
        backoff.SettleIfStable(Start + Seconds(31));

        Assert.Null(backoff.BeforeConnect(Start + Seconds(31)));   // that session was no flap
        backoff.Failed();
        Assert.Equal(Seconds(6), backoff.Delay);                   // and the ladder restarts at the floor
    }

    /// <summary>Where the flap window ends. A session exactly as long as the window counts as stable;
    /// one a tenth of a second shorter is still a flap and still escalates.</summary>
    [Fact]
    public void TheFlapWindowIsInclusiveAtItsEdge()
    {
        var atTheEdge = new MqttReconnectBackoff();
        atTheEdge.Failed();
        atTheEdge.Failed();
        atTheEdge.Connected(Start);
        atTheEdge.SettleIfStable(Start + Seconds(30));
        Assert.Null(atTheEdge.BeforeConnect(Start + Seconds(30)));

        var justInside = new MqttReconnectBackoff();
        justInside.Failed();
        justInside.Failed();
        justInside.Connected(Start);
        justInside.SettleIfStable(Start + Seconds(29.9));
        Assert.Equal(Seconds(24), justInside.BeforeConnect(Start + Seconds(29.9)));
    }

    /// <summary>A resume from standby killed the socket, so the session that ended with it was not
    /// the broker's doing. Charging it as a flap would leave a machine waiting after every lid
    /// opening.</summary>
    [Fact]
    public void AResumeFromStandbyForgetsTheSessionAndReturnsToTheFloor()
    {
        var backoff = new MqttReconnectBackoff();
        for (int round = 0; round < 3; round++) backoff.Failed();
        backoff.Connected(Start);

        backoff.Resume();

        Assert.Null(backoff.BeforeConnect(Start + Seconds(1)));   // a one-second session, and no charge
        Assert.Equal(Seconds(3), backoff.Delay);
    }

    /// <summary>No mixture of failed rounds and flaps escalates past the cap.</summary>
    [Fact]
    public void NoSequenceOfFailuresAndFlapsEverWaitsLongerThanTheCap()
    {
        var backoff = new MqttReconnectBackoff();
        var now = Start;

        for (int round = 0; round < 40; round++)
        {
            if (round % 3 == 0)
            {
                backoff.Connected(now);
                now += Seconds(2);
                backoff.BeforeConnect(now);
            }
            else
                backoff.Failed();

            Assert.True(backoff.Delay <= Seconds(60), $"round {round} waits {backoff.Delay}");
        }

        Assert.Equal(Seconds(60), backoff.Delay);
    }

    /// <summary>What may cut a wait short. MQTTnet raises its disconnect event for a connect that
    /// never succeeded as well, with the "was connected" flag clear; waking on that skips the wait
    /// the round just earned and turns the backoff into near-continuous reconnect hammering.</summary>
    [Fact]
    public void OnlyALostSessionWakesTheWaitAndAFailedConnectDoesNot()
    {
        Assert.True(MqttConnection.ShouldWakeOnDisconnect(enabled: true, clientWasConnected: true));
        Assert.False(MqttConnection.ShouldWakeOnDisconnect(enabled: true, clientWasConnected: false));
        Assert.False(MqttConnection.ShouldWakeOnDisconnect(enabled: false, clientWasConnected: true));
        Assert.False(MqttConnection.ShouldWakeOnDisconnect(enabled: false, clientWasConnected: false));
    }

    /// <summary>A shutdown that arrives mid-wait has to end the wait. One that runs its timer out
    /// regardless holds the process open for however long the wait happened to be — up to the cap.
    /// </summary>
    [Fact]
    public async Task AShutdownDuringTheWaitEndsItRatherThanRunningTheTimerOut()
    {
        using var connection = Unconfigured();
        using var stopping = new CancellationTokenSource();

        var elapsed = Stopwatch.StartNew();
        var waiting = connection.DelayOrWake(Seconds(60), stopping.Token);
        await Task.Delay(50);
        await stopping.CancelAsync();

        Assert.False(await waiting);   // false is what breaks the maintain loop
        Assert.True(elapsed.Elapsed < Seconds(5), $"the wait took {elapsed.Elapsed}");
    }

    /// <summary>A wait asked for after the shutdown does not start one.</summary>
    [Fact]
    public async Task AWaitAskedForAfterTheShutdownDoesNotStart()
    {
        using var connection = Unconfigured();
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var elapsed = Stopwatch.StartNew();

        Assert.False(await connection.DelayOrWake(Seconds(60), stopping.Token));
        Assert.True(elapsed.Elapsed < Seconds(5), $"the wait took {elapsed.Elapsed}");
    }

    /// <summary>A drop cuts the wait short, so a broker that came back is not left waiting out a
    /// delay set while it was down. Unlike a shutdown, the loop carries on.</summary>
    [Fact]
    public async Task AWakeEndsTheWaitEarlyWithoutEndingTheLoop()
    {
        using var connection = Unconfigured();
        using var stopping = new CancellationTokenSource();

        var elapsed = Stopwatch.StartNew();
        var waiting = connection.DelayOrWake(Seconds(60), stopping.Token);
        await Task.Delay(50);
        connection.Wake();

        Assert.True(await waiting);
        Assert.True(elapsed.Elapsed < Seconds(5), $"the wait took {elapsed.Elapsed}");
    }

    /// <summary>The wake is spent once it has fired. A wake left standing makes every later wait
    /// return at once, which is the backoff gone rather than shortened.</summary>
    [Fact]
    public async Task AWakeIsSpentOnceItHasFiredAndTheNextWaitStillWaits()
    {
        using var connection = Unconfigured();
        using var stopping = new CancellationTokenSource();

        var first = connection.DelayOrWake(Seconds(60), stopping.Token);
        connection.Wake();
        Assert.True(await first);

        var elapsed = Stopwatch.StartNew();
        Assert.True(await connection.DelayOrWake(Seconds(0.4), stopping.Token));
        Assert.True(elapsed.Elapsed >= Seconds(0.3), $"the second wait returned after {elapsed.Elapsed}");
    }
}
