using MQTTnet;
using MQTTnet.Protocol;

namespace ZeroZero.Mqtt;

/// <summary>Everything that touches MQTTnet, in one place. Internal, so no client-library type
/// reaches a public signature; the module's own endpoint, reason-code and QoS types stand in front
/// of it.</summary>
internal static class MqttClientWiring
{
    /// <summary>Applies a resolved endpoint and its certificate trust to a client options builder.
    /// The one place either transport is wired, so the live publisher and the probe configure the
    /// client identically.</summary>
    internal static MqttClientOptionsBuilder WithEndpoint(
        this MqttClientOptionsBuilder builder, MqttEndpointAddress address, MqttCertificateTrust trust)
    {
        builder = builder.WithProtocolVersion((MQTTnet.Formatter.MqttProtocolVersion)(int)MqttProtocol.Version);

        builder = address.Transport == MqttTransport.Tcp
            ? builder.WithTcpServer(address.Host, address.Port)
            : builder.WithWebSocketServer(o => o.WithUri(address.Uri));

        // TLS on the WebSocket side is the URI scheme's business, so the address has already
        // resolved whether the link is encrypted at all.
        return address.Encrypted ? builder.WithTlsOptions(o => Apply(o, address, trust)) : builder;
    }

    private static void Apply(
        MqttClientTlsOptionsBuilder options, MqttEndpointAddress address, MqttCertificateTrust trust)
    {
        options.UseTls(true);

        // A TCP endpoint carries no URI to take the SNI name from, and the host as typed is what the
        // certificate is expected to name.
        if (address.Transport == MqttTransport.Tcp) options.WithTargetHost(address.Host);

        // System trust leaves the platform's own validation exactly as it is: no handler, no
        // relaxation, and nothing for a later edit to loosen by accident.
        if (trust.Mode == MqttCertificateTrustMode.System) return;

        // A pinned certificate is checked by this handler alone, so the platform's verdict must not
        // reject it first. The handler is the whole test: it accepts one certificate and no other.
        options.WithAllowUntrustedCertificates(true);
        options.WithIgnoreCertificateChainErrors(true);
        options.WithCertificateValidationHandler(args =>
            trust.Accepts(MqttPresentedCertificate.From(args.Certificate, args.SslPolicyErrors)));
    }

    /// <summary>Stage 2 of the probe: does the broker accept a CONNECT with these credentials.</summary>
    /// <remarks>Same option shape as the live connection's, transport and trust included, so a
    /// passing probe says something about the connection the publisher will make — minus the will
    /// and retain machinery, because this session must leave no trace on the broker.</remarks>
    internal static async Task<MqttProbeResult> ProbeConnectAsync(
        MqttProbeTarget target, MqttEndpointAddress address,
        CancellationToken budget, CancellationToken ct)
    {
        using var client = new MqttClientFactory().CreateMqttClient();
        try
        {
            var options = new MqttClientOptionsBuilder()
                .WithEndpoint(address, target.Trust)
                .WithClientId(target.ClientId)
                .WithCleanSession()
                .WithTimeout(MqttProbe.Timeout);
            if (!string.IsNullOrEmpty(target.Username))
                options = options.WithCredentials(target.Username, target.Password);

            var result = await client.ConnectAsync(options.Build(), budget).ConfigureAwait(false);
            return MqttProbe.ClassifyConnack(ConnackCode(result), result?.ReasonString);
        }
        catch (OperationCanceledException) { return MqttProbe.Cancelled(ct); }
        catch (Exception ex) { return MqttProbe.ClassifyConnectException(ex, ct); }
        finally
        {
            // Not on the budget token: a cancelled budget must still let the throwaway session close
            // rather than leaving the broker to time it out.
            try { if (client.IsConnected) await client.DisconnectAsync().ConfigureAwait(false); }
            catch { /* the session is going away either way */ }
        }
    }

    /// <summary>The CONNACK reason code, as the protocol numbers it. A missing result is an
    /// unspecified error rather than a success.</summary>
    internal static MqttConnackCode ConnackCode(MqttClientConnectResult? result) =>
        (MqttConnackCode)(int)(result?.ResultCode ?? MqttClientConnectResultCode.UnspecifiedError);

    /// <summary>The PUBACK reason code, as the protocol numbers it.</summary>
    internal static MqttPubackCode PubackCode(MqttClientPublishResult? result) =>
        (MqttPubackCode)(int)(result?.ReasonCode ?? MqttClientPublishReasonCode.UnspecifiedError);

    internal static MqttQualityOfServiceLevel Qos(MqttQos qos) => (MqttQualityOfServiceLevel)(int)qos;
}
