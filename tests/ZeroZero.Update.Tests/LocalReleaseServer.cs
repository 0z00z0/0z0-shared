using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ZeroZero.Update.Tests;

/// <summary>A loopback HTTP/1.1 server over a TcpListener: one response per path, written exactly
/// as the test says — status, headers, body, and a declared length the body may fall short of, so
/// a download that ends early is a real socket closing early. Nothing here reaches the internet.</summary>
internal sealed class LocalReleaseServer : IDisposable
{
    internal sealed record Response(
        int Status,
        byte[] Body,
        IReadOnlyDictionary<string, string>? Headers = null,
        long? DeclaredLength = null,
        TimeSpan? Delay = null);

    internal sealed record Request(string Path, IReadOnlyDictionary<string, string> Headers);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, Func<Response>> _routes = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<Request> _requests = new();

    public LocalReleaseServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture)}/");
        _ = Task.Run(AcceptAsync);
    }

    public Uri BaseUri { get; }

    public IReadOnlyList<Request> Requests => _requests.ToArray();

    public int RequestsFor(string path) => _requests.Count(request => request.Path == path);

    public void Map(string path, Response response) => _routes[path] = () => response;

    public void Map(string path, Func<Response> response) => _routes[path] = response;

    public void MapJson(string path, string json) =>
        Map(path, new Response(200, Encoding.UTF8.GetBytes(json), new Dictionary<string, string> { ["Content-Type"] = "application/json; charset=utf-8" }));

    public void MapFile(string path, byte[] bytes, long? declaredLength = null) =>
        Map(path, new Response(200, bytes, new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" }, declaredLength));

    /// <summary>The release JSON in the shape GitHub's API returns, with the assets served from
    /// this server under <c>/download/{name}</c>.</summary>
    public string ReleaseJson(string tag, string body, params (string Name, long Size)[] assets) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            name = tag,
            body,
            html_url = "https://example.invalid/releases/tag/" + tag,
            published_at = "2026-09-02T00:00:00Z",
            draft = false,
            prerelease = false,
            assets = assets.Select(asset => new
            {
                name = asset.Name,
                size = asset.Size,
                browser_download_url = new Uri(BaseUri, "download/" + asset.Name).AbsoluteUri,
            }).ToArray(),
        });

    /// <summary>A port with nothing behind it.</summary>
    public static Uri ClosedPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();

                var received = new MemoryStream();
                byte[] chunk = new byte[1024];
                while (!EndsHeaders(received))
                {
                    int read = await stream.ReadAsync(chunk, _stop.Token);
                    if (read == 0) return;
                    received.Write(chunk, 0, read);
                }

                string[] lines = Encoding.ASCII.GetString(received.ToArray()).Split("\r\n");
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines.Skip(1))
                {
                    int colon = line.IndexOf(':', StringComparison.Ordinal);
                    if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }
                string path = lines[0].Split(' ')[1];
                _requests.Enqueue(new Request(path, headers));

                Response response = _routes.TryGetValue(path, out Func<Response>? factory)
                    ? factory()
                    : new Response(404, Encoding.UTF8.GetBytes("not here"));
                if (response.Delay is { } delay) await Task.Delay(delay, _stop.Token);

                var head = new StringBuilder();
                head.Append("HTTP/1.1 ").Append(response.Status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Reason(response.Status)).Append("\r\n");
                head.Append("Content-Length: ").Append((response.DeclaredLength ?? response.Body.Length).ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                head.Append("Connection: close\r\n");
                foreach (KeyValuePair<string, string> header in response.Headers ?? new Dictionary<string, string>())
                    head.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
                head.Append("\r\n");

                await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), _stop.Token);
                await stream.WriteAsync(response.Body, _stop.Token);
                await stream.FlushAsync(_stop.Token);
                client.Client.Shutdown(SocketShutdown.Send);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client went away, or the server is stopping; either ends the exchange.
        }
    }

    private static bool EndsHeaders(MemoryStream received)
    {
        if (received.Length < 4) return false;
        ReadOnlySpan<byte> bytes = received.GetBuffer().AsSpan(0, (int)received.Length);
        return bytes.IndexOf("\r\n\r\n"u8) >= 0;
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        403 => "Forbidden",
        404 => "Not Found",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        _ => "Status",
    };

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
