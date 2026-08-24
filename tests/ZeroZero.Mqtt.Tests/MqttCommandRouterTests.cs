using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>Topic to target, and what becomes of a payload the entity will not act on. A refusal
/// publishes nothing, changes nothing and clamps nothing.</summary>
public class MqttCommandRouterTests
{
    private const string Root = "exampleapp";
    private const string Device = "exampleapp_desk01";

    private static MqttCommandRouting Route(
        MqttCommandRouter router, string entityId, string payload = "ON", bool retained = false) =>
        router.Route(Root, Device, MqttTopics.Command(Root, Device, entityId), retained, payload);

    [Fact]
    public void ATopicOutsideTheCommandSubtreeIsNotACommandAtAll()
    {
        var router = new MqttCommandRouter([]);

        var routing = router.Route(Root, Device, MqttTopics.Channel(Root, Device, "quiet_mode"), false, "ON");

        Assert.Null(routing.EntityId);
    }

    [Fact]
    public void ARetainedInboundMessageIsDroppedBeforeTheEntitySeesIt()
    {
        // A command is an event, not state. With a clean session plus resubscribe-on-connect, a
        // retained payload would re-fire on every reconnect.
        bool ran = false;
        var router = new MqttCommandRouter([
            new("quiet_mode", _ => MqttCommandVerdict.Accept(() => ran = true)),
        ]);

        var routing = Route(router, "quiet_mode", retained: true);

        Assert.Equal(MqttCommandOutcome.Retained, routing.Verdict.Outcome);
        Assert.False(ran);
    }

    [Fact]
    public void ATopicNoEntityOwnsIsUnrecognised()
    {
        var router = new MqttCommandRouter([]);

        Assert.Equal(MqttCommandOutcome.Unrecognised, Route(router, "quiet_mode").Verdict.Outcome);
    }

    [Fact]
    public void AnAcceptedPayloadCarriesTheWorkButDoesNotRunIt()
    {
        bool ran = false;
        var router = new MqttCommandRouter([
            new("quiet_mode", _ => MqttCommandVerdict.Accept(() => ran = true)),
        ]);

        var routing = Route(router, "quiet_mode");

        Assert.True(routing.Verdict.IsAccepted);
        Assert.False(ran);
    }

    [Fact]
    public async Task TheAcceptedWorkIsAsynchronousAndTakesACancellationToken()
    {
        CancellationToken seen = default;
        var router = new MqttCommandRouter([
            new("restart", _ => MqttCommandVerdict.Accept(async ct =>
            {
                seen = ct;
                await Task.Yield();
            })),
        ]);
        using var cts = new CancellationTokenSource();

        await Route(router, "restart", "PRESS").Verdict.Run!(cts.Token);

        Assert.Equal(cts.Token, seen);
    }

    [Theory]
    [InlineData(MqttCommandOutcome.Malformed)]
    [InlineData(MqttCommandOutcome.OutOfRange)]
    [InlineData(MqttCommandOutcome.NotAnOption)]
    [InlineData(MqttCommandOutcome.Refused)]
    public void ARefusalCarriesNoWorkToRun(MqttCommandOutcome outcome)
    {
        var router = new MqttCommandRouter([
            new("poll_interval", _ => new MqttCommandVerdict(outcome, "not now")),
        ]);

        var verdict = Route(router, "poll_interval", "9999").Verdict;

        Assert.False(verdict.IsAccepted);
        Assert.Null(verdict.Run);
    }

    [Fact]
    public void ARefusalCarriesTheApplicationsOwnWording()
    {
        // The module never composes this: only the application knows why a value it understands is
        // one it will not act on.
        var router = new MqttCommandRouter([
            new("power", _ => MqttCommandVerdict.Refuse("'Shutdown' is not available while the machine is off.")),
        ]);

        Assert.Equal("'Shutdown' is not available while the machine is off.",
            Route(router, "power", "Shutdown").Verdict.Detail);
    }

    [Fact]
    public void AnAcceptedVerdictWithNoWorkIsNotAccepted()
    {
        var router = new MqttCommandRouter([
            new("quiet_mode", _ => new MqttCommandVerdict(MqttCommandOutcome.Accepted)),
        ]);

        Assert.False(Route(router, "quiet_mode").Verdict.IsAccepted);
    }

    [Fact]
    public void Replace_RoutesToTheNewHandlersFromTheNextMessageOn()
    {
        var router = new MqttCommandRouter([new("old", _ => MqttCommandVerdict.Accept(() => { }))]);

        router.Replace([new("new", _ => MqttCommandVerdict.Accept(() => { }))]);

        Assert.Equal(MqttCommandOutcome.Unrecognised, Route(router, "old").Verdict.Outcome);
        Assert.Equal(MqttCommandOutcome.Accepted, Route(router, "new").Verdict.Outcome);
        Assert.Equal(["new"], router.EntityIds);
    }

    [Fact]
    public void TheEntityIsGivenTheRawPayloadToJudge()
    {
        string? seen = null;
        var router = new MqttCommandRouter([
            new("profile", payload => { seen = payload; return MqttCommandVerdict.NotAnOption(); }),
        ]);

        Route(router, "profile", " Office ");

        Assert.Equal(" Office ", seen);
    }
}
