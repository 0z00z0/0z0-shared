using Xunit;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>What each command entity makes of an inbound payload. A payload can arrive from anything
/// holding a broker connection, so the bounds a receiver's own control respects are enforced here as
/// well — and a refusal publishes nothing, changes nothing and clamps nothing.</summary>
public class CommandAcceptanceTests
{
    [Fact]
    public void ASwitchTakesItsDeclaredPair()
    {
        bool? applied = null;
        var entity = Sample.Switch(apply: on => applied = on);

        Assert.Equal(MqttCommandOutcome.Accepted, entity.Accept("ON").Outcome);
        entity.Accept("OFF").Run!(CancellationToken.None).Wait();

        Assert.False(applied);
    }

    [Fact]
    public void ASwitchRefusesAnythingElse()
    {
        var verdict = Sample.Switch().Accept("yes");

        Assert.Equal(MqttCommandOutcome.Malformed, verdict.Outcome);
        Assert.NotEqual("", verdict.Detail);
    }

    [Fact]
    public void ANumberRefusesWhatIsNotANumber() =>
        Assert.Equal(MqttCommandOutcome.Malformed, Sample.Number().Accept("soon").Outcome);

    [Theory]
    [InlineData("4.9")]
    [InlineData("300.1")]
    [InlineData("-1")]
    public void ANumberRefusesWhatIsOutsideItsBounds(string payload)
    {
        double? applied = null;
        var verdict = Sample.Number(apply: v => applied = v).Accept(payload);

        Assert.Equal(MqttCommandOutcome.OutOfRange, verdict.Outcome);
        Assert.Null(applied);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("300")]
    [InlineData("42.5")]
    public void ANumberTakesWhatIsInside(string payload) =>
        Assert.Equal(MqttCommandOutcome.Accepted, Sample.Number().Accept(payload).Outcome);

    [Fact]
    public void ANumberReadsThePayloadAsAMachineWouldWriteIt()
    {
        double? applied = null;
        Sample.Number(apply: v => applied = v).Accept("42.5").Run!(CancellationToken.None).Wait();

        Assert.Equal(42.5, applied);
    }

    [Fact]
    public void ASelectTakesOneOfItsCurrentOptions()
    {
        string? applied = null;
        var entity = Sample.Select(apply: v => applied = v);

        entity.Accept("Home").Run!(CancellationToken.None).Wait();
        Assert.Equal("Home", applied);
    }

    [Fact]
    public void ASelectRefusesWhatIsNotOnOfferNow()
    {
        List<string> options = ["Office", "Home"];
        var entity = Sample.Select(options: () => options);

        Assert.Equal(MqttCommandOutcome.Accepted, entity.Accept("Home").Outcome);

        options.Remove("Home");
        Assert.Equal(MqttCommandOutcome.NotAnOption, entity.Accept("Home").Outcome);
    }

    [Fact]
    public void ASelectRefusesItsOwnSentinel()
    {
        // It is offered so the topic can always carry a value, not so it can be asked for.
        var verdict = Sample.Select().Accept(MqttSelect.DefaultNoOption);

        Assert.Equal(MqttCommandOutcome.NotAnOption, verdict.Outcome);
    }

    [Fact]
    public void AButtonTakesOnlyItsOwnPayload()
    {
        int presses = 0;
        var button = Sample.Button(press: () => presses++);

        Assert.Equal(MqttCommandOutcome.Malformed, button.Accept("ON").Outcome);
        Assert.Equal(MqttCommandOutcome.Malformed, button.Accept("press").Outcome);

        button.Accept(MqttButton.DefaultPress).Run!(CancellationToken.None).Wait();
        Assert.Equal(1, presses);
    }

    [Fact]
    public void TextRefusesWhatIsTooLongOrTooShort()
    {
        var entity = new MqttText
        {
            EntityId = "note",
            Name = "Note",
            Read = () => "",
            Apply = _ => MqttCommandVerdict.Accept(() => { }),
            MinLength = 2,
            MaxLength = 4,
        };

        Assert.Equal(MqttCommandOutcome.OutOfRange, entity.Accept("a").Outcome);
        Assert.Equal(MqttCommandOutcome.OutOfRange, entity.Accept("abcde").Outcome);
        Assert.Equal(MqttCommandOutcome.Accepted, entity.Accept("ab").Outcome);
        Assert.Equal(MqttCommandOutcome.Accepted, entity.Accept("abcd").Outcome);
    }

    [Fact]
    public void ARefusalTheApplicationComposesIsCarriedVerbatim()
    {
        var entity = Sample.Switch();
        var refusing = new MqttSwitch
        {
            EntityId = entity.EntityId,
            Name = entity.Name,
            Read = () => true,
            Apply = _ => MqttCommandVerdict.Refuse("The machine is asleep."),
        };

        var verdict = refusing.Accept("ON");

        Assert.Equal(MqttCommandOutcome.Refused, verdict.Outcome);
        Assert.Equal("The machine is asleep.", verdict.Detail);
        Assert.Null(verdict.Run);
    }
}
