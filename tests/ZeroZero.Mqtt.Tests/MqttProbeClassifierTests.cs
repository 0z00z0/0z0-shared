using System.Net.Sockets;
using System.Security.Authentication;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>Turning what the OS and the broker said into a verdict a user can act on. The three that
/// must never blur together are "nothing is there", "your password is wrong" and "the certificate
/// was refused": the first is a typo, the second is a credential, and the third must never be
/// retried in clear text.</summary>
public class MqttProbeClassifierTests
{
    [Fact]
    public void ClassifyConnack_ReadsAnAcceptedSessionAsSuccess() =>
        Assert.Equal(MqttProbeOutcome.Success,
            MqttProbe.ClassifyConnack(MqttConnackCode.Success, null).Outcome);

    [Theory]
    [InlineData(MqttConnackCode.BadUserNameOrPassword)]
    [InlineData(MqttConnackCode.NotAuthorised)]
    public void ClassifyConnack_ReadsBothCredentialRefusalsTheSameWay(MqttConnackCode code)
    {
        // A broker with anonymous access disabled answers "not authorised" to a blank username,
        // which is the same user error as a wrong password.
        Assert.Equal(MqttProbeOutcome.AuthRejected, MqttProbe.ClassifyConnack(code, null).Outcome);
    }

    [Theory]
    [InlineData(MqttConnackCode.Banned)]
    [InlineData(MqttConnackCode.ClientIdentifierNotValid)]
    [InlineData(MqttConnackCode.ServerBusy)]
    public void ClassifyConnack_TreatsAnyOtherRefusalAsAnAnswer(MqttConnackCode code)
    {
        // The broker spoke, so the verdict can never be "unreachable".
        Assert.Equal(MqttProbeOutcome.Rejected, MqttProbe.ClassifyConnack(code, null).Outcome);
    }

    [Fact]
    public void ClassifyConnack_CarriesTheBrokersOwnReasonIntoTheDetail() =>
        Assert.Contains("banned here",
            MqttProbe.ClassifyConnack(MqttConnackCode.Banned, "banned here").Detail);

    [Theory]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.ConnectionRefused)]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.HostUnreachable)]
    public void ClassifySocketError_ReadsAnOsRefusalAsUnreachable(SocketError error) =>
        Assert.Equal(MqttProbeOutcome.Unreachable, MqttProbe.ClassifySocketError(error).Outcome);

    [Fact]
    public void ClassifySocketError_KeepsATimeoutSeparateFromARefusal() =>
        Assert.Equal(MqttProbeOutcome.TimedOut,
            MqttProbe.ClassifySocketError(SocketError.TimedOut).Outcome);

    [Fact]
    public void ClassifyConnectException_ReadsAHandshakeFailureAsItsOwnVerdict()
    {
        var wrapped = new InvalidOperationException("connect failed",
            new AuthenticationException("The remote certificate is invalid."));

        Assert.Equal(MqttProbeOutcome.TlsFailed,
            MqttProbe.ClassifyConnectException(wrapped, CancellationToken.None).Outcome);
    }

    [Fact]
    public void ClassifyConnectException_PrefersASocketErrorOverTheWrappersOwnType()
    {
        var wrapped = new InvalidOperationException("connect failed",
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.Equal(MqttProbeOutcome.Unreachable,
            MqttProbe.ClassifyConnectException(wrapped, CancellationToken.None).Outcome);
    }

    [Fact]
    public void ClassifyConnectException_ReadsAnExpiredBudgetAsNoAnswer()
    {
        var timedOut = new OperationCanceledException();

        Assert.Equal(MqttProbeOutcome.TimedOut,
            MqttProbe.ClassifyConnectException(timedOut, CancellationToken.None).Outcome);
    }

    [Fact]
    public void ClassifyConnectException_TellsTheUsersCancellationFromATimeout()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = MqttProbe.ClassifyConnectException(new OperationCanceledException(), cts.Token);

        Assert.Equal(MqttProbeOutcome.Failed, result.Outcome);
        Assert.Equal("cancelled", result.Detail);
    }

    [Fact]
    public void ClassifyConnectException_NeverCarriesMoreThanTheTypeAndMessage()
    {
        var thrown = new InvalidOperationException("broker said no");

        var result = MqttProbe.ClassifyConnectException(thrown, CancellationToken.None);

        Assert.Equal("InvalidOperationException: broker said no", result.Detail);
    }

    [Fact]
    public void ProbeClientId_IsNeverThePublishersOwn()
    {
        // A broker kicks off any existing session holding the same client id, so reusing the device
        // id would drop the live connection on every button press.
        Assert.NotEqual("exampleapp_desk01", MqttProbe.ProbeClientId("exampleapp_desk01"));
    }

    [Fact]
    public void ASweepGetsAShorterBudgetThanASingleCandidate() =>
        Assert.True(MqttProbe.SweepTimeout < MqttProbe.Timeout);
}
