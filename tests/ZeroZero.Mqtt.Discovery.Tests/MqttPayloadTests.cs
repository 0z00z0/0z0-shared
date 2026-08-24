using System.Globalization;
using Xunit;

namespace ZeroZero.Mqtt.Discovery.Tests;

/// <summary>The plain values a bare topic carries. A payload is read by a machine, so the one thing
/// that must never vary is how a number looks.</summary>
public class MqttPayloadTests
{
    [Fact]
    public void Flag_IsTheDeclaredPairOrNothing()
    {
        Assert.Equal("ON", MqttPayload.Flag(true));
        Assert.Equal("OFF", MqttPayload.Flag(false));
        Assert.Null(MqttPayload.Flag(null));
    }

    [Fact]
    public void Number_UsesAPointRegardlessOfTheMachinesCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nb-NO");
            Assert.Equal("12.5", MqttPayload.Number(12.5));
            Assert.Equal("1234.5", MqttPayload.Number(1234.5));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Number_ReadsBackWhateverACommaCultureWouldMangle()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nb-NO");
            Assert.Equal(12.5, MqttPayload.ReadNumber("12.5"));
            // A decimal comma is not this wire format, and reading it as one would silently accept
            // a number a thousand times too large.
            Assert.Null(MqttPayload.ReadNumber("12,5"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Number_HasNothingToSayForAValueThatIsNotOne()
    {
        Assert.Null(MqttPayload.Number((double?)null));
        Assert.Null(MqttPayload.Number(double.NaN));
        Assert.Null(MqttPayload.Number(double.PositiveInfinity));
    }

    [Fact]
    public void Number_FormatsAnIntegerWithoutADecimalPart() =>
        Assert.Equal("30", MqttPayload.Number(30d));

    [Theory]
    [InlineData("ON", true)]
    [InlineData("on", true)]
    [InlineData("OFF", false)]
    [InlineData("off", false)]
    public void ReadFlag_TakesEitherCase(string payload, bool expected) =>
        Assert.Equal(expected, MqttPayload.ReadFlag(payload));

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("ONN")]
    public void ReadFlag_TakesNothingElse(string payload) =>
        Assert.Null(MqttPayload.ReadFlag(payload));

    [Fact]
    public void ReadFlag_HonoursADeclaredPair()
    {
        Assert.True(MqttPayload.ReadFlag("RUNNING", "RUNNING", "STOPPED"));
        Assert.Null(MqttPayload.ReadFlag("ON", "RUNNING", "STOPPED"));
    }
}
