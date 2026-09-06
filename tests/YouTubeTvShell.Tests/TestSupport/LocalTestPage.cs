using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YouTubeTvShell.Tests.TestSupport;

/// <summary>
/// Deterministic localhost-only test page for all browser automation.
/// Serves two distinct routes — /home and /other — so Esc/flow tests can
/// distinguish home from non-home by path. Query-string home markers
/// (e.g. ?home=1) are banned: the host state machine keys off exact URLs,
/// and distinct paths keep that contract honest.
///
/// No external network: binds 127.0.0.1 on an ephemeral port, serves fixed
/// strings, and never navigates anywhere. BCL TcpListener only — no packages.
/// </summary>
public sealed class LocalTestPage : IDisposable
{
    public const string HomePath = "/home";
    public const string OtherPath = "/other";

    private const string HomeHtml =
        "<!doctype html><html><head><title>YTTV Test Home</title></head>" +
        "<body><h1 data-testid=\"home-marker\">YTTV test home</h1></body></html>";

    private const string OtherHtml =
        "<!doctype html><html><head><title>YTTV Test Other</title></head>" +
        "<body><h1 data-testid=\"other-marker\">YTTV test other</h1></body></html>";

    private const string NotFoundHtml =
        "<!doctype html><html><head><title>Not found</title></head>" +
        "<body><h1 data-testid=\"host-error-marker\">Host error: unknown test route</h1></body></html>";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _disposed;

    private LocalTestPage(TcpListener listener, int port)
    {
        _listener = listener;
        Port = port;
        BaseUrl = $"http://127.0.0.1:{port}";
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string BaseUrl { get; }

    public string HomeUrl => BaseUrl + HomePath;

    public string OtherUrl => BaseUrl + OtherPath;

    private long _requestCount;

    /// <summary>Number of HTTP requests served so far. For flow assertions.</summary>
    public long RequestCount => Interlocked.Read(ref _requestCount);

    /// <summary>Starts the page on a free localhost port. Throws if it cannot bind.</summary>
    public static LocalTestPage Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new LocalTestPage(listener, port);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            _ = Task.Run(() => ServeOneAsync(client));
        }
    }

    private async Task ServeOneAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

                var requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(requestLine))
                    return;

                // Drain headers so keep-alive clients do not stall.
                string? line;
                do
                {
                    line = await reader.ReadLineAsync();
                } while (!string.IsNullOrEmpty(line));

                Interlocked.Increment(ref _requestCount);

                var path = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } parts
                    ? parts[1].Split('?', 2)[0]
                    : "/";

                string body;
                int status;
                string reason;
                if (path.Equals(HomePath, StringComparison.OrdinalIgnoreCase))
                {
                    (body, status, reason) = (HomeHtml, 200, "OK");
                }
                else if (path.Equals(OtherPath, StringComparison.OrdinalIgnoreCase))
                {
                    (body, status, reason) = (OtherHtml, 200, "OK");
                }
                else
                {
                    (body, status, reason) = (NotFoundHtml, 404, "Not Found");
                }

                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var header =
                    $"HTTP/1.1 {status} {reason}\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "Connection: close\r\n" +
                    "\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(header);
                await stream.WriteAsync(headerBytes);
                await stream.WriteAsync(bodyBytes);
                await stream.FlushAsync();
            }
            catch
            {
                // Test server: a dropped client must never fail the suite.
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
