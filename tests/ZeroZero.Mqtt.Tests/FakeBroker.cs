using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ZeroZero.Mqtt.Tests;

/// <summary>One message the broker received.</summary>
public readonly record struct BrokerPublish(string Topic, string Payload, bool Retained, MqttQos Qos);

/// <summary>The smallest MQTT 5 server that can hold a real session: it accepts a socket, answers
/// CONNECT, SUBSCRIBE, UNSUBSCRIBE, PUBLISH and PINGREQ, records what it was sent, delivers a
/// published message to whatever is subscribed to it, and can push a message back.</summary>
/// <remarks>
/// A real listener rather than a stubbed client, because what these tests are about is the wire: that
/// a refused CONNACK reason code is read rather than mistaken for a live session, and that a PUBACK
/// carrying a refusal is treated as a failed publish. Both are properties of the bytes, and a fake
/// client would assert only that the module calls the methods it calls.
/// <para>It handles one connection at a time and drops a zero-byte one, because the candidate check
/// opens and closes a socket before any MQTT is spoken. A malformed CONNECT is dropped the same way,
/// which is what makes it a faithful stand-in for a broker with no TLS on its port: the ClientHello
/// of an encrypted attempt is exactly that, and the hang-up it earns is the failure the clear-text
/// retry has to be decided from.</para>
/// <para>A publish is delivered back to the connection whose own filter matches it, with the retain
/// flag down as the protocol requires of a live delivery rather than a subscription replay. A
/// publisher subscribed to what it publishes on therefore hears itself, which is a property of every
/// broker and the one the command subtree has to be correct against. The filters live for the session
/// alone, so a reconnect starts subscribed to nothing.</para>
/// </remarks>
public sealed class FakeBroker : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<BrokerPublish> _published = new();
    private readonly ConcurrentQueue<string> _subscriptions = new();

    // What the current session is subscribed to, as against the cumulative record above: delivery is
    // about what stands now, and an unsubscribe has to be able to take a filter back out.
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.Ordinal);
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

    /// <summary>How many sockets have been accepted. More of these than <see cref="Connects"/> means
    /// something opened a socket and spoke no MQTT over it, which is what the stage-1 check is.</summary>
    public int Accepts => _accepts;

    private int _connects;
    private int _accepts;

    /// <summary>What CONNECT is answered with.</summary>
    public MqttConnackCode Connack { get; set; }

    /// <summary>What a publish to a given topic is acknowledged with. The default takes everything.</summary>
    public Func<string, MqttPubackCode> PubackFor { get; set; } = _ => MqttPubackCode.Success;

    public IReadOnlyList<BrokerPublish> Published => [.. _published];

    /// <summary>Every filter that was ever subscribed, in order. Separate from what the session is
    /// subscribed to now, which is what delivery goes by: an unsubscribe takes a filter out of the
    /// second and not out of this.</summary>
    public IReadOnlyList<string> Subscriptions => [.. _subscriptions];

    /// <summary>The last payload retained on a topic, or null if nothing was published to it.</summary>
    public string? LastPayload(string topic) =>
        _published.Where(p => p.Topic == topic).Select(p => p.Payload).LastOrDefault();

    public int CountOn(string topic) => _published.Count(p => p.Topic == topic);

    /// <summary>Pushes a message to the connected client, as a broker delivering a subscription.</summary>
    public async Task SendAsync(string topic, string payload, bool retained = false)
    {
        var stream = _stream ?? throw new InvalidOperationException("No client is connected.");
        await WriteAsync(stream, PublishPacket(topic, payload, retained));
    }

    private static byte[] PublishPacket(string topic, string payload, bool retained)
    {
        var body = new List<byte>();
        WriteString(body, topic);
        body.Add(0);                                    // no properties
        body.AddRange(Encoding.UTF8.GetBytes(payload));

        var packet = new List<byte> { (byte)(0x30 | (retained ? 0x01 : 0x00)) };
        WriteRemainingLength(packet, body.Count);
        packet.AddRange(body);
        return [.. packet];
    }

    /// <summary>MQTT topic-filter matching, enough of it for the filters these tests subscribe:
    /// <c>+</c> stands for one level and <c>#</c> for the rest.</summary>
    private static bool Matches(string filter, string topic)
    {
        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');

        for (int i = 0; i < filterLevels.Length; i++)
        {
            if (filterLevels[i] == "#") return i == filterLevels.Length - 1;
            if (i >= topicLevels.Length) return false;
            if (filterLevels[i] == "+") continue;
            if (!string.Equals(filterLevels[i], topicLevels[i], StringComparison.Ordinal)) return false;
        }

        return filterLevels.Length == topicLevels.Length;
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
                Interlocked.Increment(ref _accepts);
                using var stream = client.GetStream();
                _stream = stream;
                _active.Clear();                  // a session starts subscribed to nothing
                try { await SessionAsync(stream); }
                catch { /* the client went away */ }
                _active.Clear();
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
                    // The reserved header flags of a CONNECT must be zero, and a broker closes the
                    // socket on one where they are not rather than answering. That is what a TLS
                    // ClientHello arriving on a plain port is from here: its first byte, 0x16, reads
                    // as a CONNECT carrying flags 6.
                    if ((header & 0x0F) != 0) { await DrainAsync(stream); return; }
                    Interlocked.Increment(ref _connects);
                    await WriteAsync(stream, ConnackPacket());
                    break;
                case 3: await OnPublishAsync(stream, header, body); break;
                case 8: await WriteAsync(stream, SubackPacket(body)); break;
                case 10: await WriteAsync(stream, UnsubackPacket(body)); break;
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

        string payload = Encoding.UTF8.GetString(body, i, body.Length - i);
        _published.Enqueue(new(topic, payload, retained, qos));

        if (qos != MqttQos.AtMostOnce)
        {
            // A reason code needs the long form: packet id, reason code, and an empty property block.
            byte reason = (byte)PubackFor(topic);
            await WriteAsync(stream, [0x40, 0x04, (byte)(packetId >> 8), (byte)(packetId & 0xFF), reason, 0x00]);
        }

        // A live delivery carries the retain flag down whatever the publisher asked for: the flag is
        // set only on a message sent because a subscription is new.
        if (_active.Keys.Any(filter => Matches(filter, topic)))
            await WriteAsync(stream, PublishPacket(topic, payload, retained: false));
    }

    /// <summary>Takes the rest of a packet the session is abandoning, so the close that follows is a
    /// shutdown rather than a reset. A socket closed with bytes still unread sends a reset, and the
    /// client would report a socket error where a real broker's clean close gives an end of stream —
    /// the difference the clear-text retry has to be decided across.</summary>
    private async Task DrainAsync(NetworkStream stream)
    {
        var scrap = new byte[1024];
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
        while (DateTime.UtcNow < deadline && !_stop.IsCancellationRequested)
        {
            if (!stream.DataAvailable) await Task.Delay(20, _stop.Token);
            else if (await stream.ReadAsync(scrap, _stop.Token) == 0) return;   // the client went first
        }
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
            string filter = ReadString(body, ref i);
            _subscriptions.Enqueue(filter);
            _active[filter] = 0;
            granted.Add((byte)(body[i++] & 0x03));   // grant the QoS asked for
        }

        return AckPacket(0x90, packetId, granted);
    }

    /// <summary>UNSUBSCRIBE takes the filters back out of what is delivered on. Its body is a packet
    /// id, a property block and then bare filter strings — no QoS byte, which is the whole difference
    /// from SUBSCRIBE's.</summary>
    private byte[] UnsubackPacket(byte[] body)
    {
        int i = 0;
        int packetId = (body[i] << 8) | body[i + 1];
        i += 2;
        SkipProperties(body, ref i);

        var removed = new List<byte>();
        while (i < body.Length)
        {
            _active.TryRemove(ReadString(body, ref i), out _);
            removed.Add(0x00);                    // success
        }

        return AckPacket(0xB0, packetId, removed);
    }

    // SUBACK and UNSUBACK share a shape: the packet id, an empty property block, and one reason code
    // per filter.
    private static byte[] AckPacket(byte header, int packetId, IReadOnlyList<byte> reasons)
    {
        var packet = new List<byte> { header };
        WriteRemainingLength(packet, 3 + reasons.Count);
        packet.Add((byte)(packetId >> 8));
        packet.Add((byte)(packetId & 0xFF));
        packet.Add(0x00);                         // no properties
        packet.AddRange(reasons);
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
