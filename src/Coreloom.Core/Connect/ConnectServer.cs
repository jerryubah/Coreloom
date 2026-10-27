using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Coreloom.Core.Diagnostics;

namespace Coreloom.Core.Connect;

/// <summary>What the server needs to know about this computer and where its state lives.</summary>
public sealed class ConnectServerOptions
{
    public required int Port { get; init; }

    /// <summary>The computer's name as phones show it ("JERRY-PC", "Jerry's MacBook Pro").</summary>
    public required string ComputerName { get; init; }

    /// <summary>"windows" or "macos".</summary>
    public required string Platform { get; init; }

    public required string AppVersion { get; init; }

    public required PairedDeviceStore Store { get; init; }

    public required PairingWindow Pairing { get; init; }

    /// <summary>The phone app's files (normally <see cref="WebAssets.Embedded"/>).</summary>
    public required WebAssets Assets { get; init; }

    public Func<DateTimeOffset>? Clock { get; init; }

    /// <summary>How long a live request waits for the next reading before answering "nothing new".</summary>
    public TimeSpan LongPoll { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Coreloom Connect: serves the phone app to the local network and the readings to paired phones.
/// <list type="bullet">
/// <item><c>GET /</c> and the app's files, <c>GET /api/info</c> (name, version, whether pairing is open).</item>
/// <item><c>POST /api/pair/start</c>, <c>/api/pair/finish</c>: pairing (see <see cref="ConnectCrypto"/>).</item>
/// <item><c>POST /api/session</c>: a paired phone starts a session; <c>/api/live</c> waits for the next reading (sealed
/// with the session key); <c>/api/setup-link</c> makes a one-time pairing link for a Home Screen copy; <c>/api/bye</c>
/// ends the session and <c>/api/forget</c> unpairs the phone.</item>
/// </list>
/// Phones only read: nothing on the computer can be changed through Connect.
/// </summary>
public sealed class ConnectServer : IAsyncDisposable
{
    public const int MaximumSessions = 32;
    public const int MaximumPendingPairings = 16;
    private const string LogCategory = "Connect";
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SessionIdle = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FreshSnapshot = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OverlayGrace = TimeSpan.FromSeconds(10);

    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self'; " +
        "connect-src 'self'; manifest-src 'self'; media-src 'self' blob:; worker-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; object-src 'none'";

    private readonly ConnectServerOptions _options;
    private readonly Func<DateTimeOffset> _now;
    private readonly MiniHttpServer _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Timer _housekeeping;
    private long _sequence;
    private Snapshot? _latest;
    private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string[] _connected = [];
    private bool _overlayWatched;

    private ConnectServer(ConnectServerOptions options)
    {
        _options = options;
        _now = options.Clock ?? (() => DateTimeOffset.UtcNow);
        _http = new MiniHttpServer(HandleAsync);
        _housekeeping = new Timer(_ => Housekeeping(), null, Timeout.Infinite, Timeout.Infinite);
        options.Store.Changed += OnStoreChanged;
    }

    /// <summary>Raised (on a background thread) when phones connect or disconnect.</summary>
    public event Action? ConnectedChanged;

    /// <summary>Raised (on a background thread) when a phone starts or stops showing its overlay.</summary>
    public event Action? OverlayWatchedChanged;

    /// <summary>A phone is showing its overlay tab: frame rates are worth measuring and readings worth sending often.</summary>
    public bool OverlayWatched
    {
        get
        {
            lock (_gate)
            {
                return _overlayWatched;
            }
        }
    }

    public int Port => _http.Port;

    /// <summary>A phone is watching: readings are worth building.</summary>
    public bool HasViewers
    {
        get
        {
            lock (_gate)
            {
                var now = _now();
                return _sessions.Values.Any(s => s.Confirmed && now - s.LastSeen < ConnectedWindow);
            }
        }
    }

    /// <summary>The ids of the paired devices connected now.</summary>
    public IReadOnlyList<string> ConnectedDevices
    {
        get
        {
            lock (_gate)
            {
                return _connected;
            }
        }
    }

    /// <summary>Gives the first reading of a session its recent history (a JSON object), so its charts start full.</summary>
    public Func<byte[]?>? HistoryProvider { get; set; }

    /// <summary>Starts listening. Throws <see cref="System.Net.Sockets.SocketException"/> when the port is taken.</summary>
    public static ConnectServer Start(ConnectServerOptions options)
    {
        var server = new ConnectServer(options);
        try
        {
            server._http.Start(options.Port);
        }
        catch
        {
            options.Store.Changed -= server.OnStoreChanged;
            server._housekeeping.Dispose();
            throw;
        }

        server._housekeeping.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        Log.Info(LogCategory, $"Listening on port {server.Port} ({options.Assets.Count} app files).");
        return server;
    }

    /// <summary>The latest readings (a JSON object) for the phones watching.</summary>
    public void Publish(byte[] snapshotJson)
    {
        TaskCompletionSource next;
        lock (_gate)
        {
            _latest = new Snapshot(++_sequence, snapshotJson, _now());
            next = _next;
            _next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        next.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _options.Store.Changed -= OnStoreChanged;
        await _housekeeping.DisposeAsync().ConfigureAwait(false);
        await _http.DisposeAsync().ConfigureAwait(false);
        lock (_gate)
        {
            foreach (var pending in _pending.Values)
            {
                pending.Key.Dispose();
            }

            _pending.Clear();
            _sessions.Clear();
            _next.TrySetResult();
        }

        _options.Store.Save();
        Log.Info(LogCategory, "Stopped.");
    }

    // ---- Routing ----------------------------------------------------------------------------------------------------

    private async Task<ConnectResponse> HandleAsync(ConnectRequest request, CancellationToken token)
    {
        if (!IsAllowedHost(request.Header("host")))
        {
            return ConnectResponse.Error(421, "host");
        }

        if (request.Path.StartsWith("/api/", StringComparison.Ordinal))
        {
            var response = request.Method == "GET" && request.Path == "/api/info"
                ? Info()
                : request.Method != "POST"
                    ? ConnectResponse.Error(405, "method")
                    : request.Path switch
                    {
                        "/api/pair/start" => PairStart(request),
                        "/api/pair/finish" => PairFinish(request),
                        "/api/session" => OpenSession(request),
                        "/api/live" => await LiveAsync(request, token).ConfigureAwait(false),
                        "/api/setup-link" => SetupLink(request),
                        "/api/bye" => Bye(request),
                        "/api/forget" => Forget(request),
                        _ => ConnectResponse.Error(404, "not-found"),
                    };
            response.Headers["Cache-Control"] = "no-store";
            return response;
        }

        if (request.Method is not ("GET" or "HEAD"))
        {
            return ConnectResponse.Error(405, "method");
        }

        return Static(request);
    }

    private ConnectResponse Static(ConnectRequest request)
    {
        if (!_options.Assets.TryGet(request.Path, out var asset))
        {
            return new ConnectResponse { Status = 404, ContentType = "text/plain; charset=utf-8", Body = "Not found"u8.ToArray() };
        }

        if (request.Header("if-none-match") == asset.ETag)
        {
            var notModified = ConnectResponse.Empty(304);
            notModified.Headers["ETag"] = asset.ETag;
            return notModified;
        }

        bool gzip = asset.Gzip is not null && (request.Header("accept-encoding") ?? string.Empty).Contains("gzip", StringComparison.OrdinalIgnoreCase);
        var response = new ConnectResponse
        {
            ContentType = asset.ContentType,
            Body = request.Method == "HEAD" ? [] : gzip ? asset.Gzip! : asset.Body,
        };
        response.Headers["ETag"] = asset.ETag;
        response.Headers["Cache-Control"] = "no-cache";
        if (gzip)
        {
            response.Headers["Content-Encoding"] = "gzip";
        }

        if (asset.Gzip is not null)
        {
            response.Headers["Vary"] = "Accept-Encoding";
        }

        if (asset.ContentType.StartsWith("text/html", StringComparison.Ordinal))
        {
            response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            response.Headers["X-Frame-Options"] = "DENY";
        }

        return response;
    }

    /// <summary>
    /// The Host header must name this computer (an IP address, a .local name, localhost or its own name), so a web
    /// page elsewhere can't reach Coreloom through a domain name pointed at a local address (DNS rebinding).
    /// </summary>
    internal bool IsAllowedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        string name = host.Trim();
        if (name.StartsWith('['))
        {
            int end = name.IndexOf(']');
            return end > 0 && IPAddress.TryParse(name[1..end], out _);
        }

        int colon = name.LastIndexOf(':');
        if (colon >= 0)
        {
            name = name[..colon];
        }

        name = name.TrimEnd('.');
        if (IPAddress.TryParse(name, out _) || name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string firstLabel = name.Split('.')[0];
        string computer = _options.ComputerName.Split('.')[0].Replace(' ', '-');
        return firstLabel.Equals(computer, StringComparison.OrdinalIgnoreCase)
            || firstLabel.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private ConnectResponse Info()
    {
        var pairing = _options.Pairing;
        string json = JsonSerializer.Serialize(new
        {
            app = "Coreloom",
            version = _options.AppVersion,
            platform = _options.Platform,
            name = _options.ComputerName,
            pc = _options.Store.PcId,
            pairing = new { link = pairing.Accepts(PairingKind.Link), code = pairing.Accepts(PairingKind.Code), paused = pairing.IsPaused },
        });
        return ConnectResponse.Json(200, json);
    }

    // ---- Pairing ----------------------------------------------------------------------------------------------------

    private ConnectResponse PairStart(ConnectRequest request)
    {
        if (ParseBody(request) is not { } body)
        {
            return ConnectResponse.Error(400, "bad-request");
        }

        var kind = String(body, "kind") == "code" ? PairingKind.Code : PairingKind.Link;
        if (ConnectCrypto.FromBase64Url(String(body, "pub")) is not { Length: ConnectCrypto.PublicKeySize } phonePublic || phonePublic[0] != 0x04)
        {
            return ConnectResponse.Error(400, "bad-key");
        }

        if (_options.Pairing.IsPaused)
        {
            return ConnectResponse.Error(429, "paused");
        }

        if (!_options.Pairing.Accepts(kind))
        {
            return ConnectResponse.Error(409, "closed");
        }

        var key = new ConnectCrypto.EphemeralKey();
        string id = ConnectCrypto.ToBase64Url(ConnectCrypto.RandomBytes(16));
        lock (_gate)
        {
            var now = _now();
            foreach (var stale in _pending.Where(p => now - p.Value.Created > PendingLifetime).Select(p => p.Key).ToArray())
            {
                _pending[stale].Key.Dispose();
                _pending.Remove(stale);
            }

            while (_pending.Count >= MaximumPendingPairings)
            {
                var oldest = _pending.OrderBy(p => p.Value.Created).First();
                oldest.Value.Key.Dispose();
                _pending.Remove(oldest.Key);
            }

            _pending[id] = new Pending(kind, phonePublic, key, now);
        }

        return ConnectResponse.Json(200, JsonSerializer.Serialize(new
        {
            attempt = id,
            pub = ConnectCrypto.ToBase64Url(key.PublicKey),
            name = _options.ComputerName,
        }));
    }

    private ConnectResponse PairFinish(ConnectRequest request)
    {
        if (ParseBody(request) is not { } body || String(body, "attempt") is not { } id)
        {
            return ConnectResponse.Error(400, "bad-request");
        }

        Pending? pending;
        lock (_gate)
        {
            if (_pending.Remove(id, out pending) && _now() - pending.Created > PendingLifetime)
            {
                pending.Key.Dispose();
                pending = null;
            }
        }

        if (pending is null)
        {
            return ConnectResponse.Error(409, "expired");
        }

        using var key = pending.Key;
        byte[]? proof = ConnectCrypto.FromBase64Url(String(body, "proof"));
        byte[]? shared = key.SharedSecret(pending.PhonePublic);
        if (shared is null || proof is null)
        {
            return ConnectResponse.Error(400, "bad-key");
        }

        foreach (byte[] secret in _options.Pairing.Candidates(pending.Kind))
        {
            var (encrypt, mac) = ConnectCrypto.PairingKeys(shared, secret, pending.PhonePublic, key.PublicKey);
            if (!ConnectCrypto.Verify(ConnectCrypto.Mac(mac, "coreloom/pair/phone"), proof))
            {
                continue;
            }

            string name = "Phone";
            if (ConnectCrypto.FromBase64Url(String(body, "device")) is { } sealedDevice
                && ConnectCrypto.Open(encrypt, sealedDevice, "coreloom/pair/device") is { } deviceJson)
            {
                try
                {
                    using var device = JsonDocument.Parse(deviceJson);
                    name = String(device.RootElement, "name") ?? name;
                }
                catch (JsonException)
                {
                    // keep the default name
                }
            }

            bool home = pending.Kind == PairingKind.Link && _options.Pairing.IsSetupLink(secret);
            _options.Pairing.Used(secret);
            var paired = _options.Store.Add(name, pending.Kind == PairingKind.Code ? "code" : home ? "home" : "qr", _now());
            byte[] result = JsonSerializer.SerializeToUtf8Bytes(new
            {
                pc = _options.Store.PcId,
                device = paired.Id,
                key = paired.Key,
                name = _options.ComputerName,
                platform = _options.Platform,
                version = _options.AppVersion,
            });
            return ConnectResponse.Json(200, JsonSerializer.Serialize(new
            {
                result = ConnectCrypto.ToBase64Url(ConnectCrypto.Seal(encrypt, result, "coreloom/pair/result")),
            }));
        }

        _options.Pairing.Failed(pending.Kind);
        Log.Info(LogCategory, $"A pairing from {request.Remote} was refused (wrong {(pending.Kind == PairingKind.Code ? "code" : "link")}).");
        return ConnectResponse.Error(403, pending.Kind == PairingKind.Code ? "code" : "link");
    }

    // ---- Sessions ---------------------------------------------------------------------------------------------------

    private ConnectResponse OpenSession(ConnectRequest request)
    {
        if (ParseBody(request) is not { } body || ConnectCrypto.FromBase64Url(String(body, "nonce")) is not { Length: 16 } phoneNonce)
        {
            return ConnectResponse.Error(400, "bad-request");
        }

        if (_options.Store.Find(String(body, "device")) is not { } device || ConnectCrypto.FromBase64Url(device.Key) is not { } deviceKey)
        {
            return ConnectResponse.Error(401, "unpaired");
        }

        byte[] pcNonce = ConnectCrypto.RandomBytes(16);
        var (encrypt, mac) = ConnectCrypto.SessionKeys(deviceKey, phoneNonce, pcNonce);
        string id = ConnectCrypto.ToBase64Url(ConnectCrypto.RandomBytes(16));
        lock (_gate)
        {
            var now = _now();
            while (_sessions.Count >= MaximumSessions)
            {
                var oldest = _sessions.Values.OrderBy(s => s.Confirmed).ThenBy(s => s.LastSeen).First();
                _sessions.Remove(oldest.Id);
            }

            _sessions[id] = new Session(id, device.Id, encrypt, mac) { LastSeen = now };
        }

        byte[] hello = JsonSerializer.SerializeToUtf8Bytes(new
        {
            name = _options.ComputerName,
            platform = _options.Platform,
            version = _options.AppVersion,
            device = device.Name,
        });
        return ConnectResponse.Json(200, JsonSerializer.Serialize(new
        {
            session = id,
            nonce = ConnectCrypto.ToBase64Url(pcNonce),
            hello = ConnectCrypto.ToBase64Url(ConnectCrypto.Seal(encrypt, hello, "coreloom/session/hello")),
        }));
    }

    /// <summary>
    /// Checks a session request: the session exists, its device is still paired, the request's HMAC (over the path, a
    /// newline and the body) is right and its counter is higher than the last one.
    /// </summary>
    private (Session Session, long Counter, JsonElement Body)? Authenticate(ConnectRequest request, out ConnectResponse? failure)
    {
        failure = null;
        if (ParseBody(request) is not { } body || String(body, "session") is not { } id
            || !body.TryGetProperty("n", out var counterElement) || !counterElement.TryGetInt64(out long counter))
        {
            failure = ConnectResponse.Error(400, "bad-request");
            return null;
        }

        Session? session;
        lock (_gate)
        {
            _sessions.TryGetValue(id, out session);
        }

        if (session is null)
        {
            failure = ConnectResponse.Error(401, "session");
            return null;
        }

        if (_options.Store.Find(session.DeviceId) is null)
        {
            lock (_gate)
            {
                _sessions.Remove(id);
            }

            failure = ConnectResponse.Error(401, "unpaired");
            return null;
        }

        byte[] signed = [.. Encoding.UTF8.GetBytes(request.Path + "\n"), .. request.Body];
        if (!ConnectCrypto.Verify(ConnectCrypto.Mac(session.MacKey, signed), ConnectCrypto.FromBase64Url(request.Header("x-coreloom-auth"))))
        {
            failure = ConnectResponse.Error(401, "auth");
            return null;
        }

        bool firstConfirmation;
        lock (_gate)
        {
            if (counter <= session.LastCounter)
            {
                failure = ConnectResponse.Error(401, "replay");
                return null;
            }

            session.LastCounter = counter;
            session.LastSeen = _now();
            firstConfirmation = !session.Confirmed;
            session.Confirmed = true;
        }

        _options.Store.Touch(session.DeviceId, _now());
        if (firstConfirmation)
        {
            UpdateConnected();
        }

        return (session, counter, body);
    }

    private async Task<ConnectResponse> LiveAsync(ConnectRequest request, CancellationToken token)
    {
        if (Authenticate(request, out var failure) is not var (session, counter, body))
        {
            return failure!;
        }

        long after = body.TryGetProperty("after", out var afterElement) && afterElement.TryGetInt64(out long a) ? a : 0;
        bool overlay = body.TryGetProperty("overlay", out var overlayElement) && overlayElement.ValueKind == JsonValueKind.True;
        lock (_gate)
        {
            // Kept while the phone keeps asking (each answer comes within the long poll), dropped when it says it left.
            session.OverlayUntil = overlay ? _now() + _options.LongPoll + OverlayGrace : DateTimeOffset.MinValue;
        }

        UpdateOverlayWatched();
        var deadline = _now() + _options.LongPoll;
        while (true)
        {
            Task next;
            Snapshot? snapshot;
            lock (_gate)
            {
                snapshot = _latest;
                next = _next.Task;
                if (snapshot is not null && snapshot.Sequence > after && (after > 0 || _now() - snapshot.At < FreshSnapshot))
                {
                    break;
                }

                if (_now() >= deadline)
                {
                    snapshot = null;
                    break;
                }

                session.LastSeen = _now(); // still connected while it waits
            }

            var remaining = deadline - _now();
            if (remaining <= TimeSpan.Zero)
            {
                continue;
            }

            await Task.WhenAny(next, Task.Delay(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5), token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }

        Snapshot? latest;
        lock (_gate)
        {
            latest = _latest;
            session.LastSeen = _now();
        }

        if (latest is null || latest.Sequence <= after || (after == 0 && _now() - latest.At >= FreshSnapshot))
        {
            return ConnectResponse.Empty(204);
        }

        using var payload = new MemoryStream();
        payload.Write("{\"seq\":"u8);
        payload.Write(Encoding.ASCII.GetBytes(latest.Sequence.ToString(CultureInfo.InvariantCulture)));
        payload.Write(",\"snapshot\":"u8);
        payload.Write(latest.Json);
        if (after == 0 && HistoryProvider?.Invoke() is { Length: > 0 } history)
        {
            payload.Write(",\"history\":"u8);
            payload.Write(history);
        }

        payload.Write("}"u8);
        return new ConnectResponse
        {
            ContentType = "application/octet-stream",
            Body = ConnectCrypto.Seal(session.EncryptKey, payload.ToArray(), $"coreloom/live\n{counter.ToString(CultureInfo.InvariantCulture)}"),
        };
    }

    private ConnectResponse SetupLink(ConnectRequest request)
    {
        if (Authenticate(request, out var failure) is not var (session, counter, _))
        {
            return failure!;
        }

        string? secret = _options.Pairing.MintSetupLink();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { secret, minutes = (int)PairingWindow.SetupLinkLifetime.TotalMinutes });
        return new ConnectResponse
        {
            ContentType = "application/octet-stream",
            Body = ConnectCrypto.Seal(session.EncryptKey, json, $"coreloom/setup\n{counter.ToString(CultureInfo.InvariantCulture)}"),
        };
    }

    private ConnectResponse Bye(ConnectRequest request)
    {
        if (Authenticate(request, out var failure) is not var (session, _, _))
        {
            return failure!;
        }

        lock (_gate)
        {
            _sessions.Remove(session.Id);
        }

        UpdateConnected();
        UpdateOverlayWatched();
        return ConnectResponse.Empty(204);
    }

    /// <summary>The phone forgets this computer: it's removed from the paired phones too.</summary>
    private ConnectResponse Forget(ConnectRequest request)
    {
        if (Authenticate(request, out var failure) is not var (session, _, _))
        {
            return failure!;
        }

        _options.Store.Remove(session.DeviceId); // ends its sessions (OnStoreChanged)
        return ConnectResponse.Empty(204);
    }

    // ---- Bookkeeping ------------------------------------------------------------------------------------------------

    private void OnStoreChanged()
    {
        // A removed device's sessions end at once.
        var paired = _options.Store.Devices.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var session in _sessions.Values.Where(s => !paired.Contains(s.DeviceId)).ToArray())
            {
                _sessions.Remove(session.Id);
            }
        }

        UpdateConnected();
        UpdateOverlayWatched();
    }

    private void Housekeeping()
    {
        lock (_gate)
        {
            var now = _now();
            foreach (var session in _sessions.Values.Where(s => now - s.LastSeen > SessionIdle).ToArray())
            {
                _sessions.Remove(session.Id);
            }

            foreach (var stale in _pending.Where(p => now - p.Value.Created > PendingLifetime).Select(p => p.Key).ToArray())
            {
                _pending[stale].Key.Dispose();
                _pending.Remove(stale);
            }
        }

        UpdateConnected();
        UpdateOverlayWatched();
    }

    private void UpdateConnected()
    {
        string[] connected;
        bool changed;
        lock (_gate)
        {
            var now = _now();
            connected = _sessions.Values
                .Where(s => s.Confirmed && now - s.LastSeen < ConnectedWindow)
                .Select(s => s.DeviceId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            changed = !connected.SequenceEqual(_connected);
            _connected = connected;
        }

        if (changed)
        {
            ConnectedChanged?.Invoke();
        }
    }

    private void UpdateOverlayWatched()
    {
        bool watched, changed;
        lock (_gate)
        {
            var now = _now();
            watched = _sessions.Values.Any(s => s.OverlayUntil > now);
            changed = watched != _overlayWatched;
            _overlayWatched = watched;
        }

        if (changed)
        {
            OverlayWatchedChanged?.Invoke();
        }
    }

    private static JsonElement? ParseBody(ConnectRequest request)
    {
        if (request.Body.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(request.Body, new JsonDocumentOptions { MaxDepth = 8 });
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? String(JsonElement? element, string name) => element is { } e ? String(e, name) : null;

    private sealed record Pending(PairingKind Kind, byte[] PhonePublic, ConnectCrypto.EphemeralKey Key, DateTimeOffset Created);

    private sealed record Snapshot(long Sequence, byte[] Json, DateTimeOffset At);

    private sealed class Session(string id, string deviceId, byte[] encryptKey, byte[] macKey)
    {
        public string Id { get; } = id;

        public string DeviceId { get; } = deviceId;

        public byte[] EncryptKey { get; } = encryptKey;

        public byte[] MacKey { get; } = macKey;

        public long LastCounter { get; set; }

        public DateTimeOffset LastSeen { get; set; }

        public bool Confirmed { get; set; }

        /// <summary>Until when the phone is showing its overlay (it says so with each live request).</summary>
        public DateTimeOffset OverlayUntil { get; set; } = DateTimeOffset.MinValue;
    }
}
