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

    /// <summary>Stands in for the client library's own communication exception: an ordinary wrapper
    /// carrying neither a socket error nor an authentication failure. No client-library type is
    /// referenced here on purpose — the module's types stand in front of that library, and what the
    /// classifier can see of the wrapper is only that it is neither of the two it used to require.</summary>
    private sealed class CommunicationException(string message, Exception inner)
        : Exception(message, inner);

    /// <summary>The failure a broker with no TLS on its port produces against a ClientHello, as
    /// measured against a real one: it reads the handshake as a malformed packet, closes the socket,
    /// and the client reports the end of the stream. Nothing in the chain names TLS at all.</summary>
    private static Exception HungUpOnTheClientHello() =>
        new CommunicationException("connect failed",
            new IOException("Received an unexpected EOF or 0 bytes from the transport stream."));

    [Fact]
    public void ClassifyConnectException_ReadsAnAuthenticationFailureWithNoWitnessAsATrustProblem()
    {
        // No witness was attached, so the only thing left to go on is that the failure was raised
        // where a certificate is checked.
        var wrapped = new InvalidOperationException("connect failed",
            new AuthenticationException("The remote certificate is invalid."));

        Assert.Equal(MqttProbeOutcome.TlsUntrusted,
            MqttProbe.ClassifyConnectException(wrapped, CancellationToken.None).Outcome);
    }

    [Fact]
    public void ClassifyConnectException_SeparatesTheTwoTlsFailuresOnTheCertificateAlone()
    {
        // The same failure both ways round: what separates them is whether the far end ever
        // presented a certificate, which the exception cannot say and the handshake can.
        var hangUp = HungUpOnTheClientHello();

        Assert.Equal(MqttProbeOutcome.TlsUntrusted,
            MqttProbe.ClassifyConnectException(hangUp, CancellationToken.None, certificatePresented: true).Outcome);
        Assert.Equal(MqttProbeOutcome.TlsUnsupported,
            MqttProbe.ClassifyConnectException(hangUp, CancellationToken.None, certificatePresented: false).Outcome);
    }

    [Fact]
    public void ClassifyConnectException_ReadsAClosedSocketOnTheClientHelloAsNoTlsThere()
    {
        // The measured case, and the one that kept an ordinary internal broker on 1883 unreachable:
        // the chain carries no socket error and no authentication failure, so a verdict resting on
        // either type falls through to a generic failure and blocks the clear-text retry.
        Assert.Equal(MqttProbeOutcome.TlsUnsupported,
            MqttProbe.ClassifyConnectException(
                HungUpOnTheClientHello(), CancellationToken.None, certificatePresented: false).Outcome);
    }

    [Theory]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    public void ClassifyConnectException_ReadsATornDownSocketOnTheClientHelloAsNoTlsThere(SocketError error)
    {
        // Some far ends abort the connection instead of closing it cleanly. Same answer: the socket
        // had already been established, and no certificate arrived over it.
        var torn = new CommunicationException("connect failed", new SocketException((int)error));

        Assert.Equal(MqttProbeOutcome.TlsUnsupported,
            MqttProbe.ClassifyConnectException(torn, CancellationToken.None, certificatePresented: false).Outcome);
    }

    [Fact]
    public void ClassifyConnectException_ReadsAStalledHandshakeAsNoTlsThere()
    {
        // A far end that takes the ClientHello and answers nothing, reported by the client's own
        // timeout rather than by the OS. Nothing secure was on offer, so the retry stays open.
        var stalled = new CommunicationException(
            "connect failed", new TimeoutException("The operation has timed out."));

        Assert.Equal(MqttProbeOutcome.TlsUnsupported,
            MqttProbe.ClassifyConnectException(stalled, CancellationToken.None, certificatePresented: false).Outcome);
    }

    [Fact]
    public void ClassifyConnectException_NeverReadsAPlainAttemptsFailureAsATlsVerdict()
    {
        // No witness means the attempt asked for no encryption, so neither TLS verdict can apply to
        // it however the failure arrived.
        Assert.Equal(MqttProbeOutcome.Failed,
            MqttProbe.ClassifyConnectException(HungUpOnTheClientHello(), CancellationToken.None).Outcome);
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, MqttProbeOutcome.Unreachable)]
    [InlineData(SocketError.HostNotFound, MqttProbeOutcome.Unreachable)]
    [InlineData(SocketError.TimedOut, MqttProbeOutcome.TimedOut)]
    public void ClassifyConnectException_KeepsAnOsVerdictAboutTheAddressWhateverTheWitnessSaw(
        SocketError error, MqttProbeOutcome expected)
    {
        // These are the failures of an attempt whose handshake never began, so an absent certificate
        // says nothing about them. Reading them as "no TLS there" would turn a filtered port into a
        // licence to retry in clear text.
        var os = new CommunicationException("connect failed", new SocketException((int)error));

        Assert.Equal(expected,
            MqttProbe.ClassifyConnectException(os, CancellationToken.None, certificatePresented: false).Outcome);
    }

    [Fact]
    public void ClassifyConnectException_LeavesAnExpiredBudgetAsNoAnswerRatherThanNoTlsThere()
    {
        // A budget that ran out cannot say whether a handshake ever began, so it keeps its own
        // verdict — which blocks the downgrade, the safe side of an unanswerable question.
        Assert.Equal(MqttProbeOutcome.TimedOut,
            MqttProbe.ClassifyConnectException(
                new OperationCanceledException(), CancellationToken.None, certificatePresented: false).Outcome);
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
