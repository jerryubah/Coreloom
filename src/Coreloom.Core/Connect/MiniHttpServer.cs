using System.Net;
using System.Net.Sockets;
using System.Text;
using Coreloom.Core.Diagnostics;

namespace Coreloom.Core.Connect;

/// <summary>An HTTP request as Coreloom Connect sees it (headers lower-cased).</summary>
public sealed class ConnectRequest
{
    public required string Method { get; init; }

    public required string Path { get; init; }

    public required string Query { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public required byte[] Body { get; init; }

    public required IPAddress Remote { get; init; }

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>A response: status, content type, body and extra headers.</summary>
public sealed class ConnectResponse
{
    public int Status { get; init; } = 200;

    public string ContentType { get; init; } = "application/json; charset=utf-8";

    public byte[] Body { get; init; } = [];

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ConnectResponse Json(int status, string json) => new() { Status = status, Body = Encoding.UTF8.GetBytes(json) };

    public static ConnectResponse Error(int status, string code) => Json(status, $"{{\"error\":\"{code}\"}}");

    public static ConnectResponse Empty(int status) => new() { Status = status, ContentType = string.Empty };
}

/// <summary>
/// A small HTTP/1.1 server on a TCP port, for Coreloom Connect only: GET and POST with a Content-Length body, keep-alive,
/// and nothing else. It answers only addresses on the local network (private IPv4 ranges, link-local, carrier-grade
/// NAT as used by VPNs such as Tailscale, IPv6 unique-local) and keeps every size and count small, so it is the same
/// on Windows and macOS and has as little to get wrong as possible.
/// </summary>
public sealed class MiniHttpServer : IAsyncDisposable
{
    public const int MaximumConnections = 32;
    public const int MaximumHeaderBytes = 16 * 1024;
    public const int MaximumBodyBytes = 32 * 1024;
    private const string LogCategory = "Connect";
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(75);

    private readonly Func<ConnectRequest, CancellationToken, Task<ConnectResponse>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private int _connections;

    public MiniHttpServer(Func<ConnectRequest, CancellationToken, Task<ConnectResponse>> handler)
    {
        _handler = handler;
    }

    public int Port { get; private set; }

    /// <summary>Listens on every interface (IPv4 and IPv6). Throws <see cref="SocketException"/> when the port is taken.</summary>
    public void Start(int port)
    {
        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.IPv6Any, port);
            listener.Server.DualMode = true;
            listener.Start(backlog: 32);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressFamilyNotSupported or SocketError.ProtocolNotSupported)
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(backlog: 32);
        }

        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // stopping
            }
        }

        _stop.Dispose();
    }

    /// <summary>
    /// True for addresses that can only be on this network or a private network: loopback, 10/8, 172.16/12, 192.168/16,
    /// 169.254/16, 100.64/10, IPv6 loopback, link-local (fe80::/10) and unique-local (fc00::/7).
    /// </summary>
    public static bool IsLocalNetwork(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] b = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || (b[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    private async Task AcceptLoopAsync()
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                Log.Warn(LogCategory, $"Accepting a connection failed ({ex.SocketErrorCode}).");
                continue;
            }

            if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IsLocalNetwork(remote.Address))
            {
                client.Dispose(); // not from the local network
                continue;
            }

            if (Interlocked.Increment(ref _connections) > MaximumConnections)
            {
                Interlocked.Decrement(ref _connections);
                client.Dispose();
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ServeAsync(client, remote.Address, token).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _connections);
                    client.Dispose();
                }
            }, CancellationToken.None);
        }
    }

    private async Task ServeAsync(TcpClient client, IPAddress remote, CancellationToken token)
    {
        client.NoDelay = true;
        using var stream = client.GetStream();
        var reader = new RequestReader(stream);
        bool first = true;
        while (!token.IsCancellationRequested)
        {
            ConnectRequest? request;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(first ? HeaderTimeout : IdleTimeout);
                try
                {
                    request = await reader.ReadAsync(remote, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
                {
                    return;
                }
                catch (BadRequestException ex)
                {
                    await WriteAsync(stream, ConnectResponse.Error(ex.Status, "bad-request"), keepAlive: false, token).ConfigureAwait(false);
                    return;
                }
            }

            if (request is null)
            {
                return; // closed
            }

            first = false;
            ConnectResponse response;
            try
            {
                response = await _handler(request, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory, $"{request.Method} {request.Path} failed.", ex);
                response = ConnectResponse.Error(500, "server");
            }

            bool keepAlive = !string.Equals(request.Header("connection"), "close", StringComparison.OrdinalIgnoreCase);
            try
            {
                await WriteAsync(stream, response, keepAlive, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                return;
            }

            if (!keepAlive)
            {
                return;
            }
        }
    }

    private static async Task WriteAsync(Stream stream, ConnectResponse response, bool keepAlive, CancellationToken token)
    {
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(Reason(response.Status)).Append("\r\n");
        if (!string.IsNullOrEmpty(response.ContentType))
        {
            head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        }

        head.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
        head.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n");
        head.Append("X-Content-Type-Options: nosniff\r\n");
        head.Append("Referrer-Policy: no-referrer\r\n");
        foreach (var (name, value) in response.Headers)
        {
            head.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        head.Append("\r\n");
        byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
        await stream.WriteAsync(headBytes, token).ConfigureAwait(false);
        if (response.Body.Length > 0)
        {
            await stream.WriteAsync(response.Body, token).ConfigureAwait(false);
        }

        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        304 => "Not Modified",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        411 => "Length Required",
        413 => "Payload Too Large",
        421 => "Misdirected Request",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        501 => "Not Implemented",
        503 => "Service Unavailable",
        _ => status >= 500 ? "Server Error" : "Error",
    };

    private sealed class BadRequestException(int status) : Exception
    {
        public int Status { get; } = status;
    }

    /// <summary>Reads requests one after another from a connection, keeping any bytes read past the end of one.</summary>
    private sealed class RequestReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[MaximumHeaderBytes];
        private int _start;
        private int _end;

        public async Task<ConnectRequest?> ReadAsync(IPAddress remote, CancellationToken token)
        {
            int headerEnd;
            while ((headerEnd = FindHeaderEnd()) < 0)
            {
                if (_start > 0)
                {
                    Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                    _end -= _start;
                    _start = 0;
                }

                if (_end == _buffer.Length)
                {
                    throw new BadRequestException(431);
                }

                int read = await stream.ReadAsync(_buffer.AsMemory(_end), token).ConfigureAwait(false);
                if (read == 0)
                {
                    if (_end > _start)
                    {
                        throw new IOException("closed mid-request");
                    }

                    return null;
                }

                _end += read;
            }

            string head = Encoding.ASCII.GetString(_buffer, _start, headerEnd - _start);
            _start = headerEnd + 4;

            string[] lines = head.Split("\r\n");
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            {
                throw new BadRequestException(400);
            }

            string method = requestLine[0];
            string target = requestLine[1];
            if (!target.StartsWith('/') || target.Length > 2048)
            {
                throw new BadRequestException(400);
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0 || headers.Count >= 64)
                {
                    throw new BadRequestException(400);
                }

                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
            }

            if (headers.ContainsKey("transfer-encoding"))
            {
                throw new BadRequestException(411);
            }

            int length = 0;
            if (headers.TryGetValue("content-length", out var lengthText)
                && (!int.TryParse(lengthText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out length) || length > MaximumBodyBytes))
            {
                throw new BadRequestException(413);
            }

            var body = new byte[length];
            int have = Math.Min(length, _end - _start);
            Buffer.BlockCopy(_buffer, _start, body, 0, have);
            _start += have;
            while (have < length)
            {
                int read = await stream.ReadAsync(body.AsMemory(have), token).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("closed mid-body");
                }

                have += read;
            }

            if (_start == _end)
            {
                _start = _end = 0;
            }

            int question = target.IndexOf('?');
            string path = question < 0 ? target : target[..question];
            string query = question < 0 ? string.Empty : target[(question + 1)..];
            return new ConnectRequest
            {
                Method = method,
                Path = Uri.UnescapeDataString(path),
                Query = query,
                Headers = headers,
                Body = body,
                Remote = remote,
            };
        }

        private int FindHeaderEnd()
        {
            for (int i = _start; i + 3 < _end; i++)
            {
                if (_buffer[i] == '\r' && _buffer[i + 1] == '\n' && _buffer[i + 2] == '\r' && _buffer[i + 3] == '\n')
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
