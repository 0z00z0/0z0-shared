using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ZeroZero.Primitives;
using ZeroZero.Update;
using ZeroZero.Update.Win32;
using ZeroZero.Update.WinUI;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// A download stopped part-way, with the real window, the real flow, the real service and a real
/// download off a loopback server. The only way to know the button reaches the downloader, and that
/// nothing is left on disk when it does.
/// </summary>
/// <remarks>
/// Two runs. <c>person</c> presses the window's own stop button; <c>caller</c> trips the token the
/// application passed in, which must answer as a cancellation rather than as the person stopping
/// it, or an application shutting down mid-download would read the run as an ordinary outcome and
/// stay up.
/// </remarks>
internal static class UpdateCancelScenario
{
    private const string ApplicationName = "Brand Test Harness";
    private const string DirectoryPrefix = "harness-update";

    private static readonly Version Running = new(1, 0, 0);

    /// <summary>The window the rig drives. Built here rather than left to
    /// <see cref="UpdateWindowPrompts"/> so its buttons can be pressed; everything below the window
    /// is the component's own.</summary>
    private static UpdateWindow? _window;

    internal static async Task RunAsync(string mode, string probePath, Action exit)
    {
        using var server = new DribblingServer();
        ReleaseInfo release = DownloadableRelease(server.BaseUri);

        var options = new UpdateOptions
        {
            RepositoryOwner = "harness",
            RepositoryName = "harness",
            ProductName = ApplicationName,
            RunningVersion = Running,
            ExpectedSigner = new ExpectedSigner("CN=Nobody"),
            DirectoryPrefix = DirectoryPrefix,
            InstallerFileName = "Harness-Setup-{version}.exe",
            Log = NullLogSink.Instance,
        };
        using var service = new UpdateService(options);

        _window = new UpdateWindow(new UpdateWindowOptions { ApplicationName = ApplicationName })
        {
            Title = "Update Cancel",
        };

        var flow = new UpdateFlow(service, new WindowPrompts(_window), new UpdateFlowOptions
        {
            Shutdown = () => { },
            Log = NullLogSink.Instance,
        });

        using var caller = new CancellationTokenSource();
        Task<UpdateFlowRun> run = flow.InstallAsync(release, caller.Token);

        // Answered through the buttons themselves rather than behind their backs, so what is
        // measured is the path a person takes.
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        Press(UpdateMessages.InstallLabel);

        await Task.Delay(TimeSpan.FromSeconds(2.5));
        long servedWhenStopped = server.BytesServed;
        if (mode == "caller") caller.Cancel();
        else Press(UpdateMessages.CancelDownloadLabel);

        string answer;
        try
        {
            UpdateFlowRun finished = await run;
            answer = finished.Result.ToString();
        }
        catch (OperationCanceledException)
        {
            answer = nameof(OperationCanceledException);
        }

        // Read after the run has ended, so a directory still being written is not counted as left
        // behind. The downloader removes the partial file and the service the directory around it.
        string[] left = Directory.GetDirectories(Path.GetTempPath(), DirectoryPrefix + "-*");
        await File.WriteAllTextAsync(probePath, string.Join(Environment.NewLine,
        [
            $"mode\t{mode}",
            $"answer\t{answer}",
            $"bytes-served-when-stopped\t{servedWhenStopped.ToString(CultureInfo.InvariantCulture)}",
            $"directories-left\t{left.Length.ToString(CultureInfo.InvariantCulture)}",
            $"files-left\t{left.Sum(directory => Directory.GetFiles(directory).Length).ToString(CultureInfo.InvariantCulture)}",
            "",
        ]));
        await File.WriteAllTextAsync(probePath + ".done", "");

        await Task.Delay(TimeSpan.FromSeconds(2));
        exit();
    }

    /// <summary>Presses the bracket button carrying that label through its automation peer, so the
    /// control's own click path runs.</summary>
    private static void Press(string label)
    {
        if (_window?.Content is not FrameworkElement root) return;
        if (FindByAutomationName(root, label) is not { } button) return;
        if (FrameworkElementAutomationPeer.CreatePeerForElement(button) is IInvokeProvider invoke) invoke.Invoke();
    }

    private static Button? FindByAutomationName(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && AutomationProperties.GetName(button) == name) return button;
            if (FindByAutomationName(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>A release whose installer is served by the loopback server, with one hash in its
    /// body so the download is reached at all.</summary>
    private static ReleaseInfo DownloadableRelease(Uri baseUri) => new(
        "v1.2.3", new Version(1, 2, 3, 0), "1.2.3", "Harness v1.2.3",
        "Harness v1.2.3\n\n**SHA256 (installer):** `AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084`",
        new Uri("https://example.invalid/releases/tag/v1.2.3"), null,
        [new ReleaseAsset("Harness-Setup-1.2.3.exe", DribblingServer.DeclaredLength, new Uri(baseUri, "installer"))]);

    /// <summary>The window as the flow's prompts, which is the three lines
    /// <see cref="UpdateWindowPrompts"/> is, with the window kept where the rig can press it.</summary>
    private sealed class WindowPrompts(UpdateWindow window) : IUpdatePrompts
    {
        public Task<InstallChoice> AskToInstallAsync(ReleaseInfo release, Version runningVersion) =>
            window.AskAsync(release, runningVersion);

        public DownloadSurface BeginDownload(ReleaseInfo release) => window.BeginDownload(release);

        public Task SayUpToDateAsync(Version runningVersion) => Task.CompletedTask;

        public Task SayNothingReleasedAsync() => Task.CompletedTask;

        public Task SayCheckFailedAsync(UpdateCheckResult result) => Task.CompletedTask;

        public Task SayCannotInstallAsync(PreparedUpdate update) =>
            window.ShowMessageAsync(UpdateMessages.CannotInstallHeadline(update),
                                    UpdateMessages.CannotInstallText(update), attention: true);

        public Task SayLaunchFailedAsync(PreparedUpdate update, LaunchResult result) =>
            window.ShowMessageAsync(UpdateMessages.LaunchFailedHeadline,
                                    UpdateMessages.LaunchFailedText(result), attention: true);

        public void Dismiss() => window.Dismiss();
    }

    /// <summary>
    /// A loopback server that promises forty megabytes and sends them a chunk at a time, slowly
    /// enough that a person can stop the download while it runs. Raw HTTP/1.1 over a TcpListener,
    /// as the component's own tests use: nothing reaches the internet and nothing needs a reserved
    /// address.
    /// </summary>
    private sealed class DribblingServer : IDisposable
    {
        internal const long DeclaredLength = 40L * 1024 * 1024;

        /// <summary>The blank line that ends a request's headers.</summary>
        private const string HeadersEnd = "\r\n\r\n";

        private static readonly byte[] Chunk = new byte[64 * 1024];

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private long _served;

        public DribblingServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture)}/");
            _ = Task.Run(AcceptAsync);
        }

        public Uri BaseUri { get; }

        public long BytesServed => Interlocked.Read(ref _served);

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = Task.Run(() => ServeAsync(client));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // Stopped; there is nothing left to serve.
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    await ReadRequestAsync(stream);

                    byte[] head = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n" +
                        $"Content-Length: {DeclaredLength.ToString(CultureInfo.InvariantCulture)}\r\n" +
                        "Content-Type: application/octet-stream\r\n" +
                        "Connection: close\r\n\r\n");
                    await stream.WriteAsync(head, _stop.Token);

                    long sent = 0;
                    while (sent < DeclaredLength && !_stop.IsCancellationRequested)
                    {
                        await stream.WriteAsync(Chunk, _stop.Token);
                        sent += Chunk.Length;
                        Interlocked.Add(ref _served, Chunk.Length);
                        // Slow enough that forty megabytes takes far longer than the run does.
                        await Task.Delay(120, _stop.Token);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    // The client went away, which is what a stopped download looks like from here.
                }
            }
        }

        /// <summary>Reads until the blank line that ends the request's headers. Nothing in the
        /// request matters here — there is one route — but a read that stops short would leave the
        /// rest of it to arrive mid-response.</summary>
        private async Task ReadRequestAsync(NetworkStream stream)
        {
            byte[] buffer = new byte[4096];
            int filled = 0;
            while (filled < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(filled), _stop.Token);
                if (read == 0) return;
                filled += read;
                if (Encoding.ASCII.GetString(buffer, 0, filled).Contains(HeadersEnd, StringComparison.Ordinal)) return;
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }
    }
}
