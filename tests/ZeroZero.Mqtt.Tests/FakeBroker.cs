using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ZeroZero.Mqtt.Tests;

/// <summary>One message the broker received.</summary>
public readonly record struct BrokerPublish(string Topic, string Payload, bool Retained, MqttQos Qos);

/// <summary>The smallest MQTT 5 server that can hold a real session: it accepts a socket, answers
/// CONNECT, SUBSCRIBE, PUBLISH and PINGREQ, records what it was sent, and can push a message back.</summary>
/// <remarks>
/// A real listener rather than a stubbed client, because what these tests are about is the wire: that
/// a refused CONNACK reason code is read rather than mistaken for a live session, and that a PUBACK
/// carrying a refusal is treated as a failed publish. Both are properties of the bytes, and a fake
/// client would assert only that the module calls the methods it calls.
/// <para>It handles one connection at a time and drops a zero-byte one, because the encrypted
/// candidate check opens and closes a socket before any MQTT is spoken.</para>
/// </remarks>
public sealed class FakeBroker : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<BrokerPublish> _published = new();
    private readonly ConcurrentQueue<string> _subscriptions = new();
    private NetworkStream? _stream;

    public FakeBroker(MqttConnackCode connack = MqttConnackCode.Success)
    {
        Connack = connack;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(ServeAsync);
    }

    public int Port { get; }

    /// <summary>How many CONNECT packets have arrived — what says whether a session was bounced.</summary>
    public int Connects => _connects;

    private int _connects;

    /// <summary>What CONNECT is answered with.</summary>
    public MqttConnackCode Connack { get; set; }

    /// <summary>What a publish to a given topic is acknowledged with. The default takes everything.</summary>
    public Func<string, MqttPubackCode> PubackFor { get; set; } = _ => MqttPubackCode.Success;

    public IReadOnlyList<BrokerPublish> Published => [.. _published];

    public IReadOnlyList<string> Subscriptions => [.. _subscriptions];

    /// <summary>The last payload retained on a topic, or null if nothing was published to it.</summary>
    public string? LastPayload(string topic) =>
        _published.Where(p => p.Topic == topic).Select(p => p.Payload).LastOrDefault();

    public int CountOn(string topic) => _published.Count(p => p.Topic == topic);

    /// <summary>Pushes a message to the connected client, as a broker delivering a subscription.</summary>
    public async Task SendAsync(string topic, string payload, bool retained = false)
    {
        var stream = _stream ?? throw new InvalidOperationException("No client is connected.");
        var body = new List<byte>();
        WriteString(body, topic);
        body.Add(0);                                    // no properties
        body.AddRange(Encoding.UTF8.GetBytes(payload));

        var packet = new List<byte> { (byte)(0x30 | (retained ? 0x01 : 0x00)) };
        WriteRemainingLength(packet, body.Count);
        packet.AddRange(body);

        await stream.WriteAsync(packet.ToArray(), _stop.Token);
        await stream.FlushAsync(_stop.Token);
    }

    /// <summary>Waits until <paramref name="condition"/> holds, or gives up. Returns whether it held —
    /// so a test asserts on the answer rather than on a sleep having been long enough.</summary>
    public static async Task<bool> WaitAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = client.GetStream();
                _stream = stream;
                try { await SessionAsync(stream); }
                catch { /* the client went away */ }
                _stream = null;
            }
        }
        catch { /* torn down by Dispose */ }
    }

    private async Task SessionAsync(NetworkStream stream)
    {
        while (!_stop.IsCancellationRequested)
        {
            if (await ReadPacketAsync(stream) is not { } packet) return;
            var (header, body) = packet;

            switch (header >> 4)
            {
                case 1:
                    Interlocked.Increment(ref _connects);
                    await WriteAsync(stream, ConnackPacket());
                    break;
                case 3: await OnPublishAsync(stream, header, body); break;
                case 8: await WriteAsync(stream, SubackPacket(body)); break;
                case 12: await WriteAsync(stream, [0xD0, 0x00]); break;   // PINGREQ → PINGRESP
                case 14: return;                                          // DISCONNECT
                default: break;
            }
        }
    }

    private async Task OnPublishAsync(NetworkStream stream, byte header, byte[] body)
    {
        var qos = (MqttQos)((header >> 1) & 0x03);
        bool retained = (header & 0x01) != 0;

        int i = 0;
        string topic = ReadString(body, ref i);
        int packetId = 0;
        if (qos != MqttQos.AtMostOnce)
        {
            packetId = (body[i] << 8) | body[i + 1];
            i += 2;
        }
        SkipProperties(body, ref i);

        _published.Enqueue(new(topic, Encoding.UTF8.GetString(body, i, body.Length - i), retained, qos));

        if (qos == MqttQos.AtMostOnce) return;

        // A reason code needs the long form: packet id, reason code, and an empty property block.
        byte reason = (byte)PubackFor(topic);
        await WriteAsync(stream, [0x40, 0x04, (byte)(packetId >> 8), (byte)(packetId & 0xFF), reason, 0x00]);
    }

    private byte[] ConnackPacket() => [0x20, 0x03, 0x00, (byte)Connack, 0x00];

    private byte[] SubackPacket(byte[] body)
    {
        int i = 0;
        int packetId = (body[i] << 8) | body[i + 1];
        i += 2;
        SkipProperties(body, ref i);

        var granted = new List<byte>();
        while (i < body.Length)
        {
            _subscriptions.Enqueue(ReadString(body, ref i));
            granted.Add((byte)(body[i++] & 0x03));   // grant the QoS asked for
        }

        var packet = new List<byte> { 0x90 };
        WriteRemainingLength(packet, 3 + granted.Count);
        packet.Add((byte)(packetId >> 8));
        packet.Add((byte)(packetId & 0xFF));
        packet.Add(0x00);                         // no properties
        packet.AddRange(granted);
        return [.. packet];
    }

    private async Task WriteAsync(NetworkStream stream, byte[] packet)
    {
        await stream.WriteAsync(packet, _stop.Token);
        await stream.FlushAsync(_stop.Token);
    }

    private async Task<(byte Header, byte[] Body)?> ReadPacketAsync(NetworkStream stream)
    {
        var one = new byte[1];
        if (await stream.ReadAsync(one, _stop.Token) == 0) return null;
        byte header = one[0];

        int length = 0, shift = 0;
        do
        {
            if (await stream.ReadAsync(one, _stop.Token) == 0) return null;
            length |= (one[0] & 0x7F) << shift;
            shift += 7;
        }
        while ((one[0] & 0x80) != 0);

        var body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = await stream.ReadAsync(body.AsMemory(read), _stop.Token);
            if (n == 0) return null;
            read += n;
        }
        return (header, body);
    }

    private static string ReadString(byte[] buffer, ref int i)
    {
        int length = (buffer[i] << 8) | buffer[i + 1];
        i += 2;
        string text = Encoding.UTF8.GetString(buffer, i, length);
        i += length;
        return text;
    }

    private static void WriteString(List<byte> buffer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        buffer.Add((byte)(bytes.Length >> 8));
        buffer.Add((byte)(bytes.Length & 0xFF));
        buffer.AddRange(bytes);
    }

    // A property block is a varint length followed by that many bytes. Nothing here reads a
    // property, so the whole block is stepped over.
    private static void SkipProperties(byte[] buffer, ref int i)
    {
        int value = 0, shift = 0;
        do
        {
            value |= (buffer[i] & 0x7F) << shift;
            shift += 7;
        }
        while ((buffer[i++] & 0x80) != 0);

        i += value;
    }

    private static void WriteRemainingLength(List<byte> buffer, int value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            buffer.Add(value > 0 ? (byte)(b | 0x80) : b);
        }
        while (value > 0);
    }

    /// <summary>A port that was bound and released, so a connect to it is refused rather than
    /// hanging.</summary>
    public static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
