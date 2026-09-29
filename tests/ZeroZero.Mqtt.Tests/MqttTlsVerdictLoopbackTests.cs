using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace ZeroZero.Mqtt.Tests;

/// <summary>The certificate verdicts against a loopback listener that really serves TLS, because what
/// has to hold is what the handshake records, not what a constructed exception looks like.</summary>
public class MqttTlsVerdictLoopbackTests
{
    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task AFailureAfterAnAcceptedCertificate_SaysWhatHappenedAndNotThatTrustFailed()
    {
        // TLS completes, then the far end refuses the WebSocket upgrade the way a proxy does.
        using var server = new TlsListener(
            "HTTP/1.1 530 Origin Unreachable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var target = new MqttProbeTarget("127.0.0.1", server.Port, "user", "placeholder",
            ClientId: "exampleapp_probe", Transport: MqttTransportMode.WebSocket,
            Encryption: MqttEncryptionMode.On, CertificateTrust: MqttCertificateTrust.AcceptAny);

        var report = await MqttProbe.RunAsync(target, CancellationToken.None);

        var attempt = Assert.Single(report.Attempts);
        Assert.True(server.Handshakes > 0, "the handshake must have completed for the certificate to count as accepted");
        Assert.Equal(MqttProbeOutcome.Failed, attempt.Outcome);
        Assert.Contains("530", attempt.Result.Detail);
        // Encryption was on offer, so the outcome must still keep a clear-text retry closed.
        Assert.False(MqttEndpointPlan.DowngradeSafe(attempt.Outcome));
    }

    [Trait(Guard.Category, Guard.Value)]
    [Fact]
    public async Task ACertificateTheTrustSettingRefuses_IsUntrustedAndNeverRetriedInClearText()
    {
        // Self-signed under the platform's own trust: presented, and refused.
        using var server = new TlsListener(answer: null);
        var target = new MqttProbeTarget("127.0.0.1", server.Port, "user", "placeholder",
            ClientId: "exampleapp_probe", Transport: MqttTransportMode.Tcp,
            Encryption: MqttEncryptionMode.Auto);

        var report = await MqttProbe.RunAsync(target, CancellationToken.None);

        var attempt = Assert.Single(report.Attempts);
        Assert.True(attempt.Candidate.Encrypted);
        Assert.Equal(MqttProbeOutcome.TlsUntrusted, attempt.Outcome);
    }

    /// <summary>Completes a TLS handshake with a self-signed certificate, then writes a fixed answer
    /// to whatever arrives and closes, or with no answer closes at once. A socket that never starts
    /// a handshake, or a client that refuses the certificate, is dropped.</summary>
    private sealed class TlsListener : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly X509Certificate2 _certificate = SelfSigned();
        private readonly byte[]? _answer;
        private int _handshakes;

        public TlsListener(string? answer)
        {
            _answer = answer is null ? null : Encoding.ASCII.GetBytes(answer);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(ServeAsync);
        }

        public int Port { get; }

        public int Handshakes => Volatile.Read(ref _handshakes);

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch { return; }
                _ = Task.Run(() => ServeOneAsync(client));
            }
        }

        private async Task ServeOneAsync(TcpClient client)
        {
            using (client)
            using (var tls = new SslStream(client.GetStream()))
            {
                try
                {
                    await tls.AuthenticateAsServerAsync(_certificate);
                    Interlocked.Increment(ref _handshakes);
                    if (_answer is null) return;

                    if (await tls.ReadAsync(new byte[4096]) == 0) return;
                    await tls.WriteAsync(_answer);
                    await tls.FlushAsync();
                }
                catch { /* the socket-only check, or a refused certificate */ }
            }
        }

        // SChannel will not serve an ephemeral key, so the certificate goes through PFX once.
        private static X509Certificate2 SelfSigned()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=broker.invalid", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var ephemeral = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _certificate.Dispose();
        }
    }
}
