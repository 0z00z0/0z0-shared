using MQTTnet;
using MQTTnet.Protocol;

namespace ZeroZero.Mqtt;

/// <summary>Whether one encrypted attempt ever saw a certificate. A far end that presented one speaks
/// TLS and something is wrong with its certificate; a far end that presented none does not speak TLS
/// on that port at all, and only the second may be retried in clear text.</summary>
/// <remarks>Recorded rather than inferred from the exception: both failures arrive as an
/// authentication failure, and the wording that would separate them comes from the platform and is
/// translated. Written from the validation callback, which runs on the handshake's thread.</remarks>
internal sealed class MqttHandshakeWitness
{
    private int _presented;

    public bool CertificatePresented => Volatile.Read(ref _presented) != 0;

    public void Saw() => Volatile.Write(ref _presented, 1);
}

/// <summary>Everything that touches MQTTnet, in one place. Internal, so no client-library type
/// reaches a public signature; the module's own endpoint, reason-code and QoS types stand in front
/// of it.</summary>
internal static class MqttClientWiring
{
    /// <summary>Applies a resolved endpoint and its certificate trust to a client options builder.
    /// The one place either transport is wired, so the live publisher and the probe configure the
    /// client identically.</summary>
    /// <param name="witness">Told when the far end presents a certificate. Null for a caller with no
    /// downgrade decision to make.</param>
    internal static MqttClientOptionsBuilder WithEndpoint(
        this MqttClientOptionsBuilder builder, MqttEndpointAddress address, MqttCertificateTrust trust,
        MqttHandshakeWitness? witness = null)
    {
        builder = builder.WithProtocolVersion((MQTTnet.Formatter.MqttProtocolVersion)(int)MqttProtocol.Version);

        builder = address.Transport == MqttTransport.Tcp
            ? builder.WithTcpServer(address.Host, address.Port)
            : builder.WithWebSocketServer(o => o.WithUri(address.Uri));

        // TLS on the WebSocket side is the URI scheme's business, so the address has already
        // resolved whether the link is encrypted at all.
        return address.Encrypted ? builder.WithTlsOptions(o => Apply(o, address, trust, witness)) : builder;
    }

    private static void Apply(
        MqttClientTlsOptionsBuilder options, MqttEndpointAddress address, MqttCertificateTrust trust,
        MqttHandshakeWitness? witness)
    {
        options.UseTls(true);

        // A TCP endpoint carries no URI to take the SNI name from, and the host as typed is what the
        // certificate is expected to name.
        if (address.Transport == MqttTransport.Tcp) options.WithTargetHost(address.Host);

        // A pinned certificate is checked by the handler alone, so the platform's verdict must not
        // reject it first. Under system trust nothing is relaxed: the handler is installed only to
        // witness the certificate, and its verdict is the platform's own answer unchanged.
        if (trust.Mode != MqttCertificateTrustMode.System)
        {
            options.WithAllowUntrustedCertificates(true);
            options.WithIgnoreCertificateChainErrors(true);
        }

        options.WithCertificateValidationHandler(args =>
        {
            var presented = MqttPresentedCertificate.From(args.Certificate, args.SslPolicyErrors);
            if (args.Certificate is not null) witness?.Saw();
            return trust.Accepts(presented);
        });
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
        var witness = address.Encrypted ? new MqttHandshakeWitness() : null;
        try
        {
            var options = new MqttClientOptionsBuilder()
                .WithEndpoint(address, target.Trust, witness)
                .WithClientId(target.ClientId)
                .WithCleanSession()
                .WithTimeout(MqttProbe.Timeout);
            if (!string.IsNullOrEmpty(target.Username))
                options = options.WithCredentials(target.Username, target.Password);

            var result = await client.ConnectAsync(options.Build(), budget).ConfigureAwait(false);
            return MqttProbe.ClassifyConnack(ConnackCode(result), result?.ReasonString);
        }
        catch (OperationCanceledException) { return MqttProbe.Cancelled(ct); }
        catch (Exception ex) { return MqttProbe.ClassifyConnectException(ex, ct, witness?.CertificatePresented); }
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
